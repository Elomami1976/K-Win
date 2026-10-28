using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using KWin.Utils;

namespace KWin.Services
{
    /// <summary>Information about the PC that does not change while K-Win runs.</summary>
    public sealed class StaticSystemInfo
    {
        public string WindowsName { get; init; } = "Windows";
        public string DisplayVersion { get; init; } = string.Empty;
        public string Build { get; init; } = string.Empty;
        public string Architecture { get; init; } = string.Empty;
        public string DeviceName { get; init; } = Environment.MachineName;
        public string? Model { get; init; }
        public string? CpuName { get; init; }
        public int LogicalProcessors { get; init; } = Environment.ProcessorCount;
        public ulong TotalRamBytes { get; init; }
        public IReadOnlyList<string> Gpus { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Reads real system information (registry, WMI, Win32 APIs). Values that cannot be read are
    /// left null/empty and shown as "not available" - nothing is estimated or invented.
    /// </summary>
    public static class SystemInfoService
    {
        public static Task<StaticSystemInfo> LoadStaticAsync() => Task.Run(LoadStatic);

        public static StaticSystemInfo LoadStatic()
        {
            string productName = "Windows", displayVersion = "", build = "", cpu = "";
            try
            {
                using var nt = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                productName = nt?.GetValue("ProductName") as string ?? "Windows";
                displayVersion = nt?.GetValue("DisplayVersion") as string ?? nt?.GetValue("ReleaseId") as string ?? "";
                string buildNumber = nt?.GetValue("CurrentBuildNumber") as string ?? Environment.OSVersion.Version.Build.ToString();
                build = nt?.GetValue("UBR") is int ubr ? $"{buildNumber}.{ubr}" : buildNumber;
            }
            catch (Exception ex) { Logger.Instance.Warn("SystemInfo", "registry", ex.Message); }

            // Windows 11 still reports "Windows 10 ..." in ProductName; the build number decides.
            if (WindowsVersionChecker.IsWindows11() && productName.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase))
                productName = "Windows 11" + productName["Windows 10".Length..];

            try
            {
                using var cpuKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                cpu = (cpuKey?.GetValue("ProcessorNameString") as string ?? "").Trim();
            }
            catch { }

            string? model = null;
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
                foreach (ManagementObject mo in searcher.Get())
                {
                    using (mo)
                    {
                        string manu = (mo["Manufacturer"] as string ?? "").Trim();
                        string mdl = (mo["Model"] as string ?? "").Trim();
                        if (IsMeaningful(mdl))
                            model = IsMeaningful(manu) && !mdl.StartsWith(manu, StringComparison.OrdinalIgnoreCase) ? $"{manu} {mdl}" : mdl;
                    }
                }
            }
            catch (Exception ex) { Logger.Instance.Warn("SystemInfo", "Win32_ComputerSystem", ex.Message); }

            var gpus = new List<string>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                foreach (ManagementObject mo in searcher.Get())
                {
                    using (mo)
                    {
                        string name = (mo["Name"] as string ?? "").Trim();
                        if (name.Length == 0) continue;
                        if (name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Remote", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!gpus.Contains(name)) gpus.Add(name);
                    }
                }
            }
            catch (Exception ex) { Logger.Instance.Warn("SystemInfo", "Win32_VideoController", ex.Message); }

            var mem = KWin.Core.MemoryManager.GetMemoryInfo();

            return new StaticSystemInfo
            {
                WindowsName = productName,
                DisplayVersion = displayVersion,
                Build = build,
                Architecture = RuntimeInformation.OSArchitecture.ToString(),
                DeviceName = Environment.MachineName,
                Model = model,
                CpuName = string.IsNullOrWhiteSpace(cpu) ? null : cpu,
                LogicalProcessors = Environment.ProcessorCount,
                TotalRamBytes = mem.Success ? mem.Data!.TotalPhysicalMB * 1024UL * 1024UL : 0,
                Gpus = gpus
            };
        }

        private static bool IsMeaningful(string s) =>
            s.Length > 0 &&
            !s.Equals("System Product Name", StringComparison.OrdinalIgnoreCase) &&
            !s.Equals("To Be Filled By O.E.M.", StringComparison.OrdinalIgnoreCase) &&
            !s.Equals("Default string", StringComparison.OrdinalIgnoreCase);

        /// <summary>Fixed, ready drives with their real sizes.</summary>
        public static List<DriveInfo> GetFixedDrives()
        {
            var list = new List<DriveInfo>();
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (d.DriveType == DriveType.Fixed && d.IsReady && d.TotalSize > 0)
                            list.Add(d);
                    }
                    catch { /* drive became unavailable */ }
                }
            }
            catch { }
            return list;
        }

        public static string SystemDriveRoot =>
            Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";

        public static string FormatBytes(double bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (bytes >= 1024 && i < units.Length - 1) { bytes /= 1024; i++; }
            return i >= 3 ? $"{bytes:0.#} {units[i]}" : $"{bytes:0} {units[i]}";
        }
    }

    /// <summary>
    /// Whole-system CPU usage from GetSystemTimes. The first sample has no baseline and returns null.
    /// </summary>
    public sealed class CpuUsageSampler
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME { public uint Low; public uint High; public ulong Value => ((ulong)High << 32) | Low; }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

        private ulong _idle, _total;
        private bool _hasBaseline;

        /// <summary>CPU usage in percent since the previous call, or null if not measurable yet.</summary>
        public double? Sample()
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user))
                return null;

            ulong idleNow = idle.Value;
            ulong totalNow = kernel.Value + user.Value; // kernel time includes idle time
            double? result = null;

            if (_hasBaseline && totalNow > _total)
            {
                double totalDelta = totalNow - _total;
                double idleDelta = idleNow - _idle;
                result = Math.Clamp((1.0 - idleDelta / totalDelta) * 100.0, 0, 100);
            }

            _idle = idleNow;
            _total = totalNow;
            _hasBaseline = true;
            return result;
        }
    }
}
