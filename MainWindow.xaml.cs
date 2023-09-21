using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SystemDiagnostics
{
    public partial class MainWindow : Window
    {
        private PerformanceCounter memoryCounter;
        private DispatcherTimer ramUsageTimer;

        private DispatcherTimer liveUpdateTimer;
        private const int LiveUpdateIntervalInSeconds = 10; // Set the update interval as needed (in seconds)

        private List<ProcessInfo> topProcesses = new List<ProcessInfo>();


        private ObservableCollection<ProcessInfo> processList = new ObservableCollection<ProcessInfo>();

        public MainWindow()
        {
            InitializeComponent();

            InitializeMemoryCounter();
            InitializeRamUsageTimer();

            CenterWindowOnScreen();
            UpdateSystemInfo();

            InitializeLiveUpdateTimer(); // Add this line to initialize the live update timer
        }


        private void InitializeMemoryCounter()
        {
            memoryCounter = new PerformanceCounter("Memory", "Available MBytes");
        }

        private void InitializeRamUsageTimer()
        {
            ramUsageTimer = new DispatcherTimer();
            ramUsageTimer.Interval = TimeSpan.FromSeconds(1);
            ramUsageTimer.Tick += (sender, e) => UpdateRamUsage();
            ramUsageTimer.Start();
        }

        private void InitializeLiveUpdateTimer()
        {
            liveUpdateTimer = new DispatcherTimer();
            liveUpdateTimer.Interval = TimeSpan.FromSeconds(LiveUpdateIntervalInSeconds);
            liveUpdateTimer.Tick += (sender, e) => UpdateTopProcesses(ResourceType.CPU); // Update the top CPU processes
            liveUpdateTimer.Start();
        }

        private void CenterWindowOnScreen()
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
        }

        public class ProcessInfo
        {
            public string Name { get; set; }
            public double CPUUsage { get; set; }
            public double RAMUsage { get; set; }
            public double StorageUsage { get; set; }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;

            StartRamUsageTimer();

            // Set the ItemsSource of the ListView to your processList collection.
        }

        private void StartRamUsageTimer()
        {
            ramUsageTimer = new DispatcherTimer();
            ramUsageTimer.Interval = TimeSpan.FromSeconds(1);
            ramUsageTimer.Tick += (sender, e) => UpdateRamUsage();
            ramUsageTimer.Start();
        }

        private void UpdateRamUsage()
        {
            float ramUsageMB = memoryCounter.NextValue();
            RamUsageTextBlock.Text = $"Available RAM: {ramUsageMB} MB";
        }

        private void UpdateSystemInfo()
        {
            topProcesses.Clear();

            // Get the list of running processes
            var processes = Process.GetProcesses()
                                   .Select(process => new ProcessInfo
                                   {
                                       Name = process.ProcessName,
                                       CPUUsage = GetCPUUsage(process),
                                       RAMUsage = GetRAMUsage(process),
                                       StorageUsage = GetStorageUsage(process),
                                   })
                                   .OrderByDescending(process => process.CPUUsage)
                                   .Take(10) // Select the top 10 processes by CPU usage
                                   .ToList();

            // Update the ListView with the new top processes
            ProcessListView.ItemsSource = processes;

            // Add the top 10 processes to the processList collection
            foreach (var process in processes)
            {
                processList.Add(process);
            }
            UpdateCpuInfo();
            UpdateMemoryInfo();
            UpdateGpuInfo();
            UpdateOsInfo();
            UpdateDiskSpaceInfo();
        }

        private void UpdateCpuInfo()
        {
            ManagementObjectSearcher cpuSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            foreach (ManagementObject obj in cpuSearcher.Get())
            {
                string cpuInfo = $"CPU: {obj["Name"]} (Cores: {obj["NumberOfCores"]})";
                CpuInfoTextBlock.Text = cpuInfo;
            }
        }

        private void UpdateMemoryInfo()
        {
            ManagementObjectSearcher memorySearcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory");
            ulong totalMemoryBytes = 0;
            foreach (ManagementObject obj in memorySearcher.Get())
            {
                totalMemoryBytes += Convert.ToUInt64(obj["Capacity"]);
            }
            double totalMemoryGB = (double)totalMemoryBytes / (1024 * 1024 * 1024);
            double totalMemoryTB = totalMemoryGB / 1024;
            string memoryInfo = $"Total Memory: {totalMemoryGB:F2} GB";
            MemoryInfoTextBlock.Text = memoryInfo;
        }

        private void UpdateGpuInfo()
        {
            ManagementObjectSearcher gpuSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            foreach (ManagementObject obj in gpuSearcher.Get())
            {
                string gpuInfo = $"GPU: {obj["Name"]}";
                GpuInfoTextBlock.Text = gpuInfo;

                // Display GPU VRAM information
                string vramInfo = $"VRAM: {FormatBytes(Convert.ToUInt64(obj["AdapterRAM"]))}";
                GpuVramInfoTextBlock.Text = vramInfo;
            }
        }

        private void UpdateOsInfo()
        {
            ManagementObjectSearcher osSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in osSearcher.Get())
            {
                string osInfo = $"OS: {obj["Caption"]} (Version: {obj["Version"]})";
                OsInfoTextBlock.Text = osInfo;
            }
        }

        private void UpdateDiskSpaceInfo()
        {
            var drives = DriveInfo.GetDrives();
            var diskSpaceList = new List<DiskSpaceInfo>();

            foreach (var drive in drives)
            {
                if (drive.DriveType == DriveType.Fixed)
                {
                    string driveLetter = drive.Name;
                    string freeSpace = $"{(double)drive.TotalFreeSpace / (1024 * 1024 * 1024):F2} GB";

                    // Calculate disk speed (read/write speed)
                    double diskSpeed = CalculateDiskSpeed(drive);

                    string diskSpeedInfo = $"{diskSpeed:F2} MB/s";

                    diskSpaceList.Add(new DiskSpaceInfo { Drive = driveLetter, FreeSpace = freeSpace, DiskSpeed = diskSpeedInfo });
                }
            }

            DiskSpaceListView.ItemsSource = diskSpaceList;
        }

        private double CalculateDiskSpeed(DriveInfo drive)
        {
            const int bufferSize = 1024 * 1024; // 1 MB buffer size
            byte[] buffer = new byte[bufferSize];
            double readSpeed = 0.0, writeSpeed = 0.0;

            try
            {
                // Measure read speed
                using (var fileStream = new FileStream(Path.Combine(drive.RootDirectory.FullName, "read_speed_test.tmp"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    while (stopwatch.ElapsedMilliseconds < 1000)
                    {
                        fileStream.Write(buffer, 0, buffer.Length);
                    }
                    stopwatch.Stop();
                    readSpeed = (double)fileStream.Length / (stopwatch.ElapsedMilliseconds * 1000); // MB/s
                }

                // Measure write speed
                using (var fileStream = new FileStream(Path.Combine(drive.RootDirectory.FullName, "write_speed_test.tmp"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    while (stopwatch.ElapsedMilliseconds < 1000)
                    {
                        fileStream.Read(buffer, 0, buffer.Length);
                    }
                    stopwatch.Stop();
                    writeSpeed = (double)fileStream.Length / (stopwatch.ElapsedMilliseconds * 1000); // MB/s
                }

                // Delete test files
                File.Delete(Path.Combine(drive.RootDirectory.FullName, "read_speed_test.tmp"));
                File.Delete(Path.Combine(drive.RootDirectory.FullName, "write_speed_test.tmp"));

                // Return the average of read and write speeds
                return (readSpeed + writeSpeed) / 2.0;
            }
            catch (Exception ex)
            {
                // Handle the exception here (e.g., log it or display an error message)
                Console.WriteLine($"An error occurred while measuring disk speed: {ex.Message}");
                return 0.0; // Return a default value or handle the error as needed
            }
        }


        private string FormatBytes(ulong bytes)
        {
            const ulong gigabyte = 1024 * 1024 * 1024;
            const ulong megabyte = 1024 * 1024;

            if (bytes >= gigabyte)
            {
                double gigabytes = (double)bytes / gigabyte;
                return $"{gigabytes:F2} GB";
            }
            else if (bytes >= megabyte)
            {
                double megabytes = (double)bytes / megabyte;
                return $"{megabytes:F2} MB";
            }
            else
            {
                return $"{bytes} bytes";
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            string cpuInfo = CpuInfoTextBlock.Text;
            string memoryInfo = MemoryInfoTextBlock.Text;
            string gpuInfo = GpuInfoTextBlock.Text;
            string diskSpaceInfo = "";

            foreach (DiskSpaceInfo item in DiskSpaceListView.Items)
            {
                diskSpaceInfo += $"{item.Drive}: {item.FreeSpace}\n";
            }

            try
            {
                Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog();
                saveFileDialog.Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
                saveFileDialog.FileName = "system_info.txt";

                if (saveFileDialog.ShowDialog() == true)
                {
                    string filePath = saveFileDialog.FileName;
                    using (StreamWriter writer = new StreamWriter(filePath))
                    {
                        writer.WriteLine("System Information Export");
                        writer.WriteLine("----------------------------");
                        writer.WriteLine(cpuInfo);
                        writer.WriteLine(memoryInfo);
                        writer.WriteLine(gpuInfo);
                        writer.WriteLine("\nDisk Space Information:");
                        writer.WriteLine(diskSpaceInfo);
                    }

                    MessageBox.Show($"System information has been exported to {filePath}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while exporting: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateSystemInfo();
        }

        private void CustomTitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void DiskSpaceListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DiskSpaceListView.SelectedItem != null)
            {
                string selectedDrive = ((DiskSpaceInfo)DiskSpaceListView.SelectedItem).Drive;
                Process.Start(selectedDrive);
            }
        }

        private class DiskSpaceInfo
        {
            public string Drive { get; set; }
            public string FreeSpace { get; set; }
            public string DiskSpeed { get; set; } // Add this property for disk speed
        }

        private void UpdateTopProcesses(ResourceType resourceType)
        {
            // Get the list of running processes and convert them to ProcessInfo objects
            var processes = Process.GetProcesses()
                                   .Select(process => new ProcessInfo
                                   {
                                       Name = process.ProcessName,
                                       CPUUsage = GetCPUUsage(process),
                                       RAMUsage = GetRAMUsage(process),
                                       StorageUsage = GetStorageUsage(process),
                                   })
                                   .ToList();

            // Choose a resource to sort by (CPU, RAM, or Storage)
            IEnumerable<ProcessInfo> sortedProcesses;

            switch (resourceType)
            {
                case ResourceType.CPU:
                    sortedProcesses = processes.OrderByDescending(process => process.CPUUsage);
                    break;
                case ResourceType.RAM:
                    sortedProcesses = processes.OrderByDescending(process => process.RAMUsage);
                    break;
                case ResourceType.Storage:
                    sortedProcesses = processes.OrderByDescending(process => process.StorageUsage);
                    break;
                default:
                    return; // Handle invalid resource type or provide a default behavior
            }

            // Select the top 10 processes
            var top10Processes = sortedProcesses.Take(10).ToList();

            // Clear the existing top processes from the UI
            processList.Clear();

            // Add the updated top processes to the collection
            foreach (var process in top10Processes)
            {
                processList.Add(process);
            }

        }


        private enum ResourceType
        {
            CPU,
            RAM,
            Storage
        }

        private void CPUButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateTopProcesses(ResourceType.CPU);
        }

        private void RAMButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateTopProcesses(ResourceType.RAM);
        }

        private void StorageButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateTopProcesses(ResourceType.Storage);
        }
        private float GetCPUUsage(Process process)
        {
            try
            {
                return process.TotalProcessorTime.Ticks / (float)TimeSpan.TicksPerSecond;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private float GetRAMUsage(Process process)
        {
            try
            {
                return process.PrivateMemorySize64 / (1024 * 1024); // Convert to MB
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private float GetStorageUsage(Process process)
        {
            // Implement a method to calculate storage usage for the process
            // You may need to estimate or gather this information from an external source
            return 0; // Replace with your implementation
        }

        private void ProcessListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ProcessListView.SelectedItem != null)
            {
                // Handle double-click action for the ProcessListView here
            }
        }
    }
}
