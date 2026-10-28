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

        /// <summary>How the memory compression state was determined.</summary>
        public enum CompressionSource { MMAgent, CompressionProcess }

        /// <summary>
        /// Reads whether memory compression is on.
        /// With administrator rights: Windows' supported Get-MMAgent (MemoryCompression property).
        /// Without: Windows runs the "Memory Compression" system process only while compression is
        /// on, and its presence is visible to normal users. (1.0 read a "DisableCompression"
        /// registry value that Windows does not use.)
        /// </summary>
        public static async Task<Result<(bool Enabled, CompressionSource Source)>> GetMemoryCompressionAsync()
        {
            if (Utils.ElevationHelper.IsAdministrator)
            {
                var r = await RunPowerShellFixedAsync("(Get-MMAgent).MemoryCompression");
                if (r.Success && ParsePowerShellBool(r.Data) is bool enabled)
                    return Result<(bool, CompressionSource)>.Ok((enabled, CompressionSource.MMAgent));
            }

            try
            {
                bool running = System.Diagnostics.Process.GetProcessesByName("Memory Compression").Length > 0 ||
                               System.Diagnostics.Process.GetProcessesByName("MemCompression").Length > 0;
                return Result<(bool, CompressionSource)>.Ok((running, CompressionSource.CompressionProcess));
            }
            catch (Exception ex)
            {
                return Result<(bool, CompressionSource)>.Fail(ex);
            }
        }

        /// <summary>
        /// Turns memory compression on or off with Windows' supported Enable-MMAgent / Disable-MMAgent.
        /// Requires administrator rights; takes effect after a restart.
        /// </summary>
        public static async Task<Result<bool>> SetMemoryCompressionAsync(bool enable)
        {
            try
            {
                Utils.DevMode.ThrowIfReadOnly("memory compression");
                if (!Utils.ElevationHelper.IsAdministrator)
                    return Result<bool>.Fail("Administrator rights are required.");

                var set = await RunPowerShellFixedAsync(enable ? "Enable-MMAgent -MemoryCompression" : "Disable-MMAgent -MemoryCompression");
                if (!set.Success) return Result<bool>.Fail(set.ErrorMessage ?? "Command failed");

                var check = await RunPowerShellFixedAsync("(Get-MMAgent).MemoryCompression");
                if (!check.Success || ParsePowerShellBool(check.Data) != enable)
                    return Result<bool>.Fail("Verification failed: Get-MMAgent does not report the new setting.");

                Logger.Instance.Info("SetMemoryCompression", "MMAgent", "success", newValue: enable.ToString());
                return Result<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("SetMemoryCompression", "MMAgent", ex);
                return Result<bool>.Fail(ex);
            }
        }

        /// <summary>"True"/"False" output of PowerShell, or null.</summary>
        public static bool? ParsePowerShellBool(string? output)
        {
            string s = (output ?? "").Trim();
            if (s.Equals("True", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Equals("False", StringComparison.OrdinalIgnoreCase)) return false;
            return null;
        }

        /// <summary>
        /// Runs one of K-Win's own fixed PowerShell commands (never user input) with the absolute
        /// System32 PowerShell path. Fails on a non-zero exit code or error output.
        /// </summary>
        private static async Task<Result<string>> RunPowerShellFixedAsync(string command)
        {
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(command);

            using var proc = Process.Start(psi);
            if (proc == null) return Result<string>.Fail("Could not start PowerShell.");
            var stdout = proc.StandardOutput.ReadToEndAsync();
            var stderr = proc.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await proc.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { proc.Kill(true); } catch { }
                return Result<string>.Fail("PowerShell timed out.");
            }
            string err = (await stderr).Trim();
            if (proc.ExitCode != 0 || err.Length > 0)
                return Result<string>.Fail(err.Length > 0 ? err : $"Exit code {proc.ExitCode}");
            return Result<string>.Ok(await stdout);
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
