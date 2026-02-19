using KWin.Models;
using KWin.Utils;

namespace KWin.Core
{
    /// <summary>
    /// Manages disk cleanup operations including temporary files, browser caches,
    /// Microsoft Store cache, and Recycle Bin. Calculates space savings and provides
    /// preview information before executing cleanup.
    /// </summary>
    public class CleanupManager
    {
        private readonly SystemTools _systemTools;

        public CleanupManager(SystemTools systemTools)
        {
            _systemTools = systemTools;
        }

        /// <summary>
        /// Calculates the total size of files in a directory (recursive).
        /// </summary>
        /// <param name="path">Directory path to measure.</param>
        /// <returns>Total size in bytes.</returns>
        public static long GetDirectorySize(string path)
        {
            if (!Directory.Exists(path)) return 0;

            long size = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { size += new FileInfo(file).Length; }
                    catch { /* Skip inaccessible files */ }
                }
            }
            catch { /* Skip inaccessible directories */ }
            return size;
        }

        /// <summary>
        /// Formats a byte count into a human-readable string.
        /// </summary>
        public static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double len = bytes;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        /// <summary>
        /// Gets the estimated space that can be freed by cleaning temp files.
        /// </summary>
        public Result<(long TotalBytes, Dictionary<string, long> Breakdown)> PreviewTempFileCleanup()
        {
            try
            {
                var breakdown = new Dictionary<string, long>();

                // Windows Temp
                string winTemp = Path.GetTempPath();
                long winTempSize = GetDirectorySize(winTemp);
                breakdown["Windows Temp"] = winTempSize;

                // User Temp (if different from GetTempPath)
                string userTemp = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");
                if (!string.Equals(userTemp, winTemp, StringComparison.OrdinalIgnoreCase))
                {
                    long userTempSize = GetDirectorySize(userTemp);
                    breakdown["User Temp"] = userTempSize;
                }

                // Windows Prefetch (read-only preview)
                string prefetch = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
                long prefetchSize = GetDirectorySize(prefetch);
                breakdown["Prefetch"] = prefetchSize;

                // Thumbnail Cache
                string thumbCache = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Windows", "Explorer");
                long thumbSize = 0;
                if (Directory.Exists(thumbCache))
                {
                    foreach (var file in Directory.EnumerateFiles(thumbCache, "thumbcache_*.db"))
                    {
                        try { thumbSize += new FileInfo(file).Length; } catch { }
                    }
                }
                if (thumbSize > 0) breakdown["Thumbnail Cache"] = thumbSize;

                // Windows Error Reports
                string werPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Windows", "WER");
                long werSize = GetDirectorySize(werPath);
                if (werSize > 0) breakdown["Error Reports"] = werSize;

                long total = breakdown.Values.Sum();
                return Result<(long, Dictionary<string, long>)>.Ok((total, breakdown));
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("PreviewTempFileCleanup", "temp folders", ex);
                return Result<(long, Dictionary<string, long>)>.Fail(ex);
            }
        }

        /// <summary>
        /// Gets the estimated space that can be freed by cleaning browser caches.
        /// </summary>
        public Result<(long TotalBytes, Dictionary<string, long> Breakdown)> PreviewBrowserCacheCleanup()
        {
            try
            {
                var breakdown = new Dictionary<string, long>();
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                // Microsoft Edge
                long edgeSize = GetChromiumBrowserCacheSize(localAppData, "Microsoft", "Edge");
                if (edgeSize > 0) breakdown["Microsoft Edge"] = edgeSize;

                // Google Chrome
                long chromeSize = GetChromiumBrowserCacheSize(localAppData, "Google", "Chrome");
                if (chromeSize > 0) breakdown["Google Chrome"] = chromeSize;

                // Brave Browser
                long braveSize = GetChromiumBrowserCacheSize(localAppData, "BraveSoftware", "Brave-Browser");
                if (braveSize > 0) breakdown["Brave"] = braveSize;

                // Opera
                string operaCache = Path.Combine(localAppData, "Opera Software", "Opera Stable", "Cache");
                string operaCodeCache = Path.Combine(localAppData, "Opera Software", "Opera Stable", "Code Cache");
                long operaSize = GetDirectorySize(operaCache) + GetDirectorySize(operaCodeCache);
                if (operaSize > 0) breakdown["Opera"] = operaSize;

                // Vivaldi
                long vivaldiSize = GetChromiumBrowserCacheSize(localAppData, "Vivaldi", path3: null);
                if (vivaldiSize > 0) breakdown["Vivaldi"] = vivaldiSize;

                // Mozilla Firefox
                string firefoxProfiles = Path.Combine(localAppData,
                    "Mozilla", "Firefox", "Profiles");
                long firefoxSize = 0;
                if (Directory.Exists(firefoxProfiles))
                {
                    foreach (var profile in Directory.GetDirectories(firefoxProfiles))
                    {
                        string ffCache = Path.Combine(profile, "cache2");
                        firefoxSize += GetDirectorySize(ffCache);
                    }
                }
                if (firefoxSize > 0) breakdown["Mozilla Firefox"] = firefoxSize;

                long total = breakdown.Values.Sum();
                return Result<(long, Dictionary<string, long>)>.Ok((total, breakdown));
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("PreviewBrowserCacheCleanup", "browser caches", ex);
                return Result<(long, Dictionary<string, long>)>.Fail(ex);
            }
        }

        /// <summary>
        /// Helper to calculate cache size for Chromium-based browsers.
        /// </summary>
        private static long GetChromiumBrowserCacheSize(string localAppData, string vendor, string? product = null, string? path3 = "User Data")
        {
            string basePath = product != null
                ? Path.Combine(localAppData, vendor, product)
                : Path.Combine(localAppData, vendor);

            if (path3 != null)
                basePath = Path.Combine(basePath, path3);

            string cache = Path.Combine(basePath, "Default", "Cache");
            string codeCache = Path.Combine(basePath, "Default", "Code Cache");
            string gpuCache = Path.Combine(basePath, "GpuCache");
            string shaderCache = Path.Combine(basePath, "ShaderCache");

            return GetDirectorySize(cache) + GetDirectorySize(codeCache)
                 + GetDirectorySize(gpuCache) + GetDirectorySize(shaderCache);
        }

        /// <summary>
        /// Cleans temporary files from Windows Temp and User Temp directories.
        /// Skips files that are in use.
        /// </summary>
        public Task<Result<(int FilesDeleted, long BytesFreed)>> CleanTempFilesAsync(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => CleanTempFilesInternal(progress, cancellationToken));
        }

        private Result<(int FilesDeleted, long BytesFreed)> CleanTempFilesInternal(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            int filesDeleted = 0;
            long bytesFreed = 0;

            try
            {
                Logger.Instance.Info("CleanTempFiles", "temp folders", "starting");

                var tempPaths = new List<string>
                {
                    Path.GetTempPath(),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Microsoft", "Windows", "WER"),
                };

                foreach (var tempPath in tempPaths.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!Directory.Exists(tempPath)) continue;

                    progress?.Report($"Cleaning: {tempPath}");

                    foreach (var file in Directory.EnumerateFiles(tempPath, "*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            if (!Validator.IsSafeToDelete(file, out _)) continue;

                            var fi = new FileInfo(file);
                            long size = fi.Length;
                            fi.Delete();
                            filesDeleted++;
                            bytesFreed += size;
                        }
                        catch
                        {
                            // File in use or access denied — skip silently
                        }
                    }

                    // Try to delete empty subdirectories
                    try
                    {
                        foreach (var dir in Directory.GetDirectories(tempPath, "*", SearchOption.AllDirectories)
                            .OrderByDescending(d => d.Length)) // Delete deepest first
                        {
                            try
                            {
                                if (Directory.GetFileSystemEntries(dir).Length == 0)
                                    Directory.Delete(dir);
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                Logger.Instance.Info("CleanTempFiles", "temp folders", "success",
                    newValue: $"Deleted {filesDeleted} files, freed {FormatSize(bytesFreed)}");

                return Result<(int, long)>.Ok((filesDeleted, bytesFreed));
            }
            catch (OperationCanceledException)
            {
                Logger.Instance.Warn("CleanTempFiles", "temp folders", "Cancelled by user");
                return Result<(int, long)>.Fail("Operation cancelled.");
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("CleanTempFiles", "temp folders", ex);
                return Result<(int, long)>.Fail(ex);
            }
        }

        /// <summary>
        /// Cleans browser cache directories for Edge, Chrome, and Firefox.
        /// Warns if browsers are running.
        /// </summary>
        public Task<Result<(int FilesDeleted, long BytesFreed, List<string> Warnings)>> CleanBrowserCachesAsync(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => CleanBrowserCachesInternal(progress, cancellationToken));
        }

        private Result<(int FilesDeleted, long BytesFreed, List<string> Warnings)> CleanBrowserCachesInternal(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            int filesDeleted = 0;
            long bytesFreed = 0;
            var warnings = new List<string>();

            try
            {
                Logger.Instance.Info("CleanBrowserCaches", "browser caches", "starting");
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                // Check running browsers
                if (SystemTools.IsProcessRunning("msedge"))
                    warnings.Add("Microsoft Edge is running — some cache files may be locked.");
                if (SystemTools.IsProcessRunning("chrome"))
                    warnings.Add("Google Chrome is running — some cache files may be locked.");
                if (SystemTools.IsProcessRunning("firefox"))
                    warnings.Add("Mozilla Firefox is running — some cache files may be locked.");
                if (SystemTools.IsProcessRunning("brave"))
                    warnings.Add("Brave is running — some cache files may be locked.");
                if (SystemTools.IsProcessRunning("opera"))
                    warnings.Add("Opera is running — some cache files may be locked.");
                if (SystemTools.IsProcessRunning("vivaldi"))
                    warnings.Add("Vivaldi is running — some cache files may be locked.");

                var cachePaths = new List<string>
                {
                    // Edge
                    Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache"),
                    Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Code Cache"),
                    Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "GpuCache"),
                    Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "ShaderCache"),
                    // Chrome
                    Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache"),
                    Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Code Cache"),
                    Path.Combine(localAppData, "Google", "Chrome", "User Data", "GpuCache"),
                    Path.Combine(localAppData, "Google", "Chrome", "User Data", "ShaderCache"),
                    // Brave
                    Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Cache"),
                    Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Code Cache"),
                    // Opera
                    Path.Combine(localAppData, "Opera Software", "Opera Stable", "Cache"),
                    Path.Combine(localAppData, "Opera Software", "Opera Stable", "Code Cache"),
                    // Vivaldi
                    Path.Combine(localAppData, "Vivaldi", "User Data", "Default", "Cache"),
                    Path.Combine(localAppData, "Vivaldi", "User Data", "Default", "Code Cache"),
                };

                // Add Firefox profile caches
                string firefoxProfiles = Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles");
                if (Directory.Exists(firefoxProfiles))
                {
                    foreach (var profile in Directory.GetDirectories(firefoxProfiles))
                    {
                        cachePaths.Add(Path.Combine(profile, "cache2"));
                    }
                }

                foreach (var cachePath in cachePaths)
                {
                    if (!Directory.Exists(cachePath)) continue;
                    cancellationToken.ThrowIfCancellationRequested();

                    progress?.Report($"Cleaning: {cachePath}");

                    foreach (var file in Directory.EnumerateFiles(cachePath, "*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            var fi = new FileInfo(file);
                            long size = fi.Length;
                            fi.Delete();
                            filesDeleted++;
                            bytesFreed += size;
                        }
                        catch { /* Locked files */ }
                    }
                }

                Logger.Instance.Info("CleanBrowserCaches", "browser caches", "success",
                    newValue: $"Deleted {filesDeleted} files, freed {FormatSize(bytesFreed)}");

                return Result<(int, long, List<string>)>.Ok((filesDeleted, bytesFreed, warnings));
            }
            catch (OperationCanceledException)
            {
                return Result<(int, long, List<string>)>.Fail("Operation cancelled.");
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("CleanBrowserCaches", "browser caches", ex);
                return Result<(int, long, List<string>)>.Fail(ex);
            }
        }

        /// <summary>
        /// Resets Microsoft Store cache using wsreset.exe (30s timeout).
        /// </summary>
        public async Task<Result<bool>> ResetStoreCacheAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                Logger.Instance.Info("ResetStoreCache", "wsreset.exe", "starting");
                return await _systemTools.ExecuteToolWithUIAsync("wsreset.exe", "", 30000, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("ResetStoreCache", "wsreset.exe", ex);
                return Result<bool>.Fail(ex);
            }
        }

        /// <summary>
        /// Runs the Disk Cleanup tool with sageset/sagerun profile.
        /// </summary>
        public async Task<Result<bool>> RunDiskCleanupAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                Logger.Instance.Info("RunDiskCleanup", "cleanmgr.exe", "starting");
                return await _systemTools.ExecuteToolWithUIAsync(
                    "cleanmgr.exe", "/sagerun:1", 120000, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("RunDiskCleanup", "cleanmgr.exe", ex);
                return Result<bool>.Fail(ex);
            }
        }

        /// <summary>
        /// Runs System File Checker (sfc /scannow). Long-running operation.
        /// </summary>
        public async Task<Result<string>> RunSystemFileCheckAsync(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Logger.Instance.Info("RunSystemFileCheck", "sfc.exe", "starting");
                progress?.Report("Running System File Checker — this may take several minutes...");

                // SFC can take a very long time — 10 minute timeout
                return await _systemTools.ExecuteToolAsync(
                    "sfc.exe", "/scannow", 600000, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("RunSystemFileCheck", "sfc.exe", ex);
                return Result<string>.Fail(ex);
            }
        }

        /// <summary>
        /// Runs DISM component cleanup.
        /// </summary>
        public async Task<Result<string>> RunDismCleanupAsync(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Logger.Instance.Info("RunDismCleanup", "dism.exe", "starting");
                progress?.Report("Running DISM cleanup — this may take several minutes...");

                return await _systemTools.ExecuteToolAsync(
                    "dism.exe", "/Online /Cleanup-Image /StartComponentCleanup",
                    600000, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("RunDismCleanup", "dism.exe", ex);
                return Result<string>.Fail(ex);
            }
        }

        /// <summary>
        /// Gets the current size and item count of the Recycle Bin.
        /// </summary>
        public static Result<(long SizeBytes, long ItemCount)> GetRecycleBinSize()
        {
            try
            {
                var info = new NativeMethods.SHQUERYRBINFO
                {
                    cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>()
                };

                uint hr = NativeMethods.SHQueryRecycleBin(null, ref info);
                if (hr == 0)
                {
                    return Result<(long, long)>.Ok((info.i64Size, info.i64NumItems));
                }

                return Result<(long, long)>.Fail($"SHQueryRecycleBin returned 0x{hr:X8}");
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("GetRecycleBinSize", "RecycleBin", ex);
                return Result<(long, long)>.Fail(ex);
            }
        }

        /// <summary>
        /// Empties the Recycle Bin for all drives.
        /// </summary>
        public Result<bool> EmptyRecycleBin()
        {
            try
            {
                Logger.Instance.Info("EmptyRecycleBin", "RecycleBin", "starting");

                // Use SHEmptyRecycleBin via P/Invoke
                uint result = NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null,
                    NativeMethods.SHERB_NOCONFIRMATION |
                    NativeMethods.SHERB_NOPROGRESSUI |
                    NativeMethods.SHERB_NOSOUND);

                // S_OK = 0, or error (0x80070002 = already empty, which is fine)
                if (result == 0 || result == 0x80070002)
                {
                    Logger.Instance.Info("EmptyRecycleBin", "RecycleBin", "success");
                    return Result<bool>.Ok(true);
                }

                Logger.Instance.Error("EmptyRecycleBin", "RecycleBin", $"HRESULT: 0x{result:X8}");
                return Result<bool>.Fail($"SHEmptyRecycleBin returned 0x{result:X8}");
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("EmptyRecycleBin", "RecycleBin", ex);
                return Result<bool>.Fail(ex);
            }
        }
    }

    /// <summary>
    /// P/Invoke declarations for native Windows APIs.
    /// </summary>
    internal static class NativeMethods
    {
        public const uint SHERB_NOCONFIRMATION = 0x00000001;
        public const uint SHERB_NOPROGRESSUI = 0x00000002;
        public const uint SHERB_NOSOUND = 0x00000004;

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern uint SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
        public struct SHQUERYRBINFO
        {
            public uint cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern uint SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);
    }
}
