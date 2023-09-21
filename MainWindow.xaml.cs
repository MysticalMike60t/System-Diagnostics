using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace SystemDiagnostics
{
    public partial class MainWindow : Window
    {
        private PerformanceCounter memoryCounter;
        private DispatcherTimer ramUsageTimer;

        private bool isDragging = false;
        private Point offset;

        public MainWindow()
        {
            InitializeComponent();

            memoryCounter = new PerformanceCounter("Memory", "Available MBytes");
            UpdateRamUsage();

            UpdateSystemInfo();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;

            StartRamUsageTimer();
        }

        private void UpdateRamUsage()
        {
            float ramUsageMB = memoryCounter.NextValue();
            RamUsageTextBlock.Text = $"Available RAM: {ramUsageMB} MB";
        }

        private void StartRamUsageTimer()
        {
            ramUsageTimer = new DispatcherTimer();
            ramUsageTimer.Interval = TimeSpan.FromSeconds(1);
            ramUsageTimer.Tick += (sender, e) => UpdateRamUsage();
            ramUsageTimer.Start();
        }

        private void UpdateSystemInfo()
        {
            ManagementObjectSearcher cpuSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            foreach (ManagementObject obj in cpuSearcher.Get())
            {
                string cpuInfo = $"CPU: {obj["Name"]} (Cores: {obj["NumberOfCores"]})";
                CpuInfoTextBlock.Text = cpuInfo;
            }

            // Get memory information
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

            ManagementObjectSearcher gpuSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            foreach (ManagementObject obj in gpuSearcher.Get())
            {
                string gpuInfo = $"GPU: {obj["Name"]}";
                GpuInfoTextBlock.Text = gpuInfo;

                // Display GPU VRAM information
                string vramInfo = $"VRAM: {FormatBytes(Convert.ToUInt64(obj["AdapterRAM"]))}";
                GpuVramInfoTextBlock.Text = vramInfo;
            }

            ManagementObjectSearcher osSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in osSearcher.Get())
            {
                string osInfo = $"OS: {obj["Caption"]} (Version: {obj["Version"]})";
                OsInfoTextBlock.Text = osInfo;
            }

            // Update disk space information
            ManagementObjectSearcher diskSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_LogicalDisk");
            var diskSpaceList = new List<DiskSpaceInfo>();

            foreach (ManagementObject obj in diskSearcher.Get())
            {
                if (obj["DriveType"].ToString() == "3")
                {
                    string driveLetter = obj["Name"].ToString();
                    ulong freeSpaceBytes = Convert.ToUInt64(obj["FreeSpace"]);

                    double freeSpaceGB = (double)freeSpaceBytes / (1024 * 1024 * 1024);

                    string freeSpace;
                    if (freeSpaceGB >= 1024)
                    {
                        double freeSpaceTB = freeSpaceGB / 1024;
                        freeSpace = $"{freeSpaceTB:F2} TB";
                    }
                    else
                    {
                        freeSpace = $"{freeSpaceGB:F2} GB";
                    }
                    diskSpaceList.Add(new DiskSpaceInfo { Drive = driveLetter, FreeSpace = freeSpace });
                }
            }
            DiskSpaceListView.ItemsSource = diskSpaceList;
        }

        private class DiskSpaceInfo
        {
            public string Drive { get; set; }
            public string FreeSpace { get; set; }
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
                // Assuming that you have a "Drive" property in your data model class
                // Replace 'YourDataModel' with the actual class name
                string selectedDrive = ((DiskSpaceInfo)DiskSpaceListView.SelectedItem).Drive;

                // Use Process.Start to open the selected drive in File Explorer
                Process.Start(selectedDrive);
            }
        }

    }
}
