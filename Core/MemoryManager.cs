using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using KWin.Models;
using KWin.Utils;

namespace KWin.Core
{
    /// <summary>
    /// RAM monitoring using built-in Windows APIs: memory info, process memory usage,
    /// SysMain/Superfetch service control, memory compression status and page file info.
    /// (The 1.0 "clear cached RAM" working-set trimming was removed in 1.5: it gives no
    /// lasting benefit because Windows pages the data straight back in.)
    /// All operations use free, built-in Windows/.NET APIs only.
    /// </summary>
    public class MemoryManager
    {
        #region Native APIs

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        #endregion

        #region Memory Info

        /// <summary>
        /// Represents a snapshot of current system memory usage.
        /// </summary>
        public class MemoryInfo
        {
            public ulong TotalPhysicalMB { get; set; }
            public ulong AvailablePhysicalMB { get; set; }
            public ulong UsedPhysicalMB { get; set; }
            public uint MemoryLoadPercent { get; set; }
            public ulong TotalPageFileMB { get; set; }
            public ulong AvailablePageFileMB { get; set; }
            public ulong UsedPageFileMB { get; set; }

            public string TotalFormatted => FormatMB(TotalPhysicalMB);
            public string AvailableFormatted => FormatMB(AvailablePhysicalMB);
            public string UsedFormatted => FormatMB(UsedPhysicalMB);

            private static string FormatMB(ulong mb)
            {
                if (mb >= 1024)
                    return $"{mb / 1024.0:F1} GB";
                return $"{mb} MB";
            }
        }

        /// <summary>
        /// Gets current system memory status using GlobalMemoryStatusEx.
        /// </summary>
        public static Result<MemoryInfo> GetMemoryInfo()
        {
            try
            {
                var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (!GlobalMemoryStatusEx(ref status))
                    return Result<MemoryInfo>.Fail("GlobalMemoryStatusEx failed.");

                ulong totalMB = status.ullTotalPhys / 1024 / 1024;
                ulong availMB = status.ullAvailPhys / 1024 / 1024;
                ulong totalPageMB = status.ullTotalPageFile / 1024 / 1024;
                ulong availPageMB = status.ullAvailPageFile / 1024 / 1024;

                return Result<MemoryInfo>.Ok(new MemoryInfo
                {
                    TotalPhysicalMB = totalMB,
                    AvailablePhysicalMB = availMB,
                    UsedPhysicalMB = totalMB - availMB,
                    MemoryLoadPercent = status.dwMemoryLoad,
                    TotalPageFileMB = totalPageMB,
                    AvailablePageFileMB = availPageMB,
                    UsedPageFileMB = totalPageMB - availPageMB
                });
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("GetMemoryInfo", "GlobalMemoryStatusEx", ex);
                return Result<MemoryInfo>.Fail(ex);
            }
        }

        #endregion

        #region Top Memory Processes

        /// <summary>
        /// Represents a process with its memory usage.
        /// </summary>
        public class ProcessMemoryInfo
        {
            public int Pid { get; set; }
            public string Name { get; set; } = string.Empty;
            public long WorkingSetMB { get; set; }
            public long PrivateBytesMB { get; set; }

            public override string ToString() => $"{Name} — {WorkingSetMB} MB";
        }

        /// <summary>
        /// Gets the top N processes by working set (RAM) usage.
        /// </summary>
        public static Result<List<ProcessMemoryInfo>> GetTopMemoryProcesses(int count = 15)
        {
            try
            {
                var processes = Process.GetProcesses()
                    .Where(p =>
                    {
                        try { return p.WorkingSet64 > 0 && !string.IsNullOrEmpty(p.ProcessName); }
                        catch { return false; }
                    })
                    .OrderByDescending(p =>
                    {
                        try { return p.WorkingSet64; } catch { return 0L; }
                    })
                    .Take(count)
                    .Select(p =>
                    {
                        try
                        {
                            return new ProcessMemoryInfo
                            {
                                Pid = p.Id,
                                Name = p.ProcessName,
                                WorkingSetMB = p.WorkingSet64 / 1024 / 1024,
                                PrivateBytesMB = p.PrivateMemorySize64 / 1024 / 1024
                            };
                        }
                        catch
                        {
                            return new ProcessMemoryInfo { Name = "(access denied)", Pid = 0 };
                        }
                    })
                    .Where(p => p.Pid != 0)
                    .ToList();

                return Result<List<ProcessMemoryInfo>>.Ok(processes);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("GetTopMemoryProcesses", "Process", ex);
                return Result<List<ProcessMemoryInfo>>.Fail(ex);
            }
        }

        #endregion

        #region SysMain / Superfetch Service

        /// <summary>
        /// Gets current SysMain (Superfetch) service status.
        /// </summary>
        public static Result<(string Status, string StartType)> GetSysMainStatus()
        {
            try
            {
                using var sc = new System.ServiceProcess.ServiceController("SysMain");
                return Result<(string, string)>.Ok((sc.Status.ToString(), sc.StartType.ToString()));
            }
            catch (Exception ex)
            {
                return Result<(string, string)>.Fail(ex);
            }
        }

        /// <summary>
        /// Starts the SysMain service. The start type is changed separately through
        /// RegistryHelper (backed up and reversible) before calling this.
        /// </summary>
        public static Task<Result<bool>> StartSysMainAsync() => SystemTools.StartServiceAsync("SysMain");

        /// <summary>
        /// Stops the SysMain service. The start type is changed separately through RegistryHelper.
        /// </summary>
        public static Task<Result<bool>> StopSysMainAsync() => SystemTools.StopServiceAsync("SysMain");

        #endregion

        #region Memory Compression

        /// <summary>
        /// Gets current memory compression status from registry.
        /// </summary>
        public static Result<bool> GetMemoryCompressionEnabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", false);

                if (key == null)
                    return Result<bool>.Ok(true); // Default is enabled

                var val = key.GetValue("DisableCompression");
                if (val is int intVal)
                    return Result<bool>.Ok(intVal == 0); // 0 = compression enabled, 1 = disabled
                
                return Result<bool>.Ok(true); // Not set = enabled by default
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("GetMemoryCompressionEnabled", "Registry", ex);
                return Result<bool>.Fail(ex);
            }
        }

        #endregion

        #region Page File Info

        /// <summary>
        /// Gets page file configuration info.
        /// </summary>
        public static Result<(bool AutoManaged, string Config)> GetPageFileInfo()
        {
            try
            {
                // Check automatic managed page file
                bool autoManaged = true;
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", false))
                {
                    if (key != null)
                    {
                        var pagingFiles = key.GetValue("PagingFiles") as string[];
                        if (pagingFiles != null && pagingFiles.Length > 0)
                        {
                            string first = pagingFiles[0];
                            // "?:\pagefile.sys" or empty means auto-managed
                            // Custom values like "C:\pagefile.sys 4096 8192" mean manual
                            if (!string.IsNullOrWhiteSpace(first))
                            {
                                var parts = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length >= 3 && int.TryParse(parts[1], out int min) && min > 0)
                                    autoManaged = false;
                            }
                        }

                        // Also check ExistingPageFiles
                        var existingFiles = key.GetValue("ExistingPageFiles") as string[];
                        string config = "No page file detected";
                        if (existingFiles != null && existingFiles.Length > 0)
                        {
                            config = string.Join(", ", existingFiles.Select(f =>
                                f.Replace(@"\??\", ""))); // Clean device path prefix
                        }

                        return Result<(bool, string)>.Ok((autoManaged, config));
                    }
                }

                return Result<(bool, string)>.Ok((true, "Default configuration"));
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("GetPageFileInfo", "Registry", ex);
                return Result<(bool, string)>.Fail(ex);
            }
        }

        /// <summary>
        /// Gets the current page file size from system info.
        /// </summary>
        public static Result<string> GetPageFileSizeInfo()
        {
            try
            {
                var memInfo = GetMemoryInfo();
                if (!memInfo.Success) return Result<string>.Fail(memInfo.ErrorMessage ?? "Failed");

                var info = memInfo.Data!;
                // Page file total includes physical RAM, so subtract to get actual page file
                ulong pageFileOnlyMB = info.TotalPageFileMB > info.TotalPhysicalMB
                    ? info.TotalPageFileMB - info.TotalPhysicalMB
                    : 0;

                return Result<string>.Ok(
                    $"Page File Size: {FormatSize(pageFileOnlyMB)} | " +
                    $"Used: {FormatSize(info.UsedPageFileMB)} | " +
                    $"Available: {FormatSize(info.AvailablePageFileMB)}");
            }
            catch (Exception ex)
            {
                return Result<string>.Fail(ex);
            }
        }

        #endregion

        #region Helpers

        /// <summary>Formats MB to human-readable string.</summary>
        public static string FormatSize(ulong mb)
        {
            if (mb >= 1024)
                return $"{mb / 1024.0:F1} GB";
            return $"{mb} MB";
        }

        #endregion
    }
}
