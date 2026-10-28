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

        /// <summary>Temp files newer than this are left alone (they are often still in use).</summary>
        public static readonly TimeSpan MinTempFileAge = TimeSpan.FromHours(24);

        /// <summary>
        /// Total size of regular files in a folder. Junctions and symbolic links are not followed.
        /// </summary>
        public static long GetDirectorySize(string path) => SafeFileWalker.GetSize(path);

        /// <summary>Formats a byte count into a human-readable string.</summary>
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

        #region Targets

        /// <summary>
        /// Temp-file cleanup targets. The same list is used for the preview and the cleanup,
        /// so the estimate matches what is actually deleted.
        /// </summary>
        public static List<CleanupTarget> GetTempTargets()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var targets = new List<CleanupTarget>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string label, string path, TimeSpan? minAge)
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                if (seen.Add(full))
                    targets.Add(new CleanupTarget(label, full, minAge));
            }

            Add("User Temp", Path.GetTempPath(), MinTempFileAge);
            Add("User Temp", Path.Combine(localAppData, "Temp"), MinTempFileAge);
            Add("Error Reports", Path.Combine(localAppData, "Microsoft", "Windows", "WER"), null);

            // The system-wide temp folder is only writable with administrator rights.
            if (ElevationHelper.IsAdministrator)
                Add("Windows Temp", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), MinTempFileAge);

            return targets;
        }

        /// <summary>Browser cache folders for every profile of the supported browsers.</summary>
        public static List<CleanupTarget> GetBrowserTargets()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var targets = new List<CleanupTarget>();

            void AddChromium(string label, string userDataDir)
            {
                if (!Directory.Exists(userDataDir)) return;
                targets.Add(new CleanupTarget(label, Path.Combine(userDataDir, "GpuCache"), null));
                targets.Add(new CleanupTarget(label, Path.Combine(userDataDir, "ShaderCache"), null));

                IEnumerable<string> profiles;
                try
                {
                    profiles = Directory.GetDirectories(userDataDir)
                        .Where(d =>
                        {
                            string n = Path.GetFileName(d);
                            return n.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                                   n.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase);
                        })
                        .ToList();
                }
                catch { profiles = Array.Empty<string>(); }

                foreach (var profile in profiles)
                {
                    targets.Add(new CleanupTarget(label, Path.Combine(profile, "Cache"), null));
                    targets.Add(new CleanupTarget(label, Path.Combine(profile, "Code Cache"), null));
                    targets.Add(new CleanupTarget(label, Path.Combine(profile, "GPUCache"), null));
                }
            }

            AddChromium("Microsoft Edge", Path.Combine(localAppData, "Microsoft", "Edge", "User Data"));
            AddChromium("Google Chrome", Path.Combine(localAppData, "Google", "Chrome", "User Data"));
            AddChromium("Brave", Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data"));
            AddChromium("Vivaldi", Path.Combine(localAppData, "Vivaldi", "User Data"));

            string opera = Path.Combine(localAppData, "Opera Software", "Opera Stable");
            targets.Add(new CleanupTarget("Opera", Path.Combine(opera, "Cache"), null));
            targets.Add(new CleanupTarget("Opera", Path.Combine(opera, "Code Cache"), null));

            string firefoxProfiles = Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles");
            if (Directory.Exists(firefoxProfiles))
            {
                try
                {
                    foreach (var profile in Directory.GetDirectories(firefoxProfiles))
                        targets.Add(new CleanupTarget("Mozilla Firefox", Path.Combine(profile, "cache2"), null));
                }
                catch { }
            }

            return targets
                .Where(t => Directory.Exists(t.Path))
                .GroupBy(t => t.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        #endregion

        #region Preview

        /// <summary>
        /// Measures what a cleanup of the targets would delete, using exactly the
        /// same file filters as <see cref="CleanTargets"/>.
        /// </summary>
        public static (long TotalBytes, Dictionary<string, long> Breakdown) Measure(IEnumerable<CleanupTarget> targets)
        {
            var breakdown = new Dictionary<string, long>();
            DateTime now = DateTime.UtcNow;
            foreach (var t in targets)
            {
                long size = SafeFileWalker.GetSize(t.Path, f => t.Includes(f, now));
                if (size <= 0) continue;
                breakdown[t.Label] = breakdown.TryGetValue(t.Label, out var existing) ? existing + size : size;
            }
            return (breakdown.Values.Sum(), breakdown);
        }

        /// <summary>Estimated space that temp-file cleanup will free.</summary>
        public Result<(long TotalBytes, Dictionary<string, long> Breakdown)> PreviewTempFileCleanup()
        {
            try
            {
                return Result<(long, Dictionary<string, long>)>.Ok(Measure(GetTempTargets()));
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("PreviewTempFileCleanup", "temp folders", ex);
                return Result<(long, Dictionary<string, long>)>.Fail(ex);
            }
        }

        /// <summary>Estimated space that browser-cache cleanup will free.</summary>
        public Result<(long TotalBytes, Dictionary<string, long> Breakdown)> PreviewBrowserCacheCleanup()
        {
            try
            {
                return Result<(long, Dictionary<string, long>)>.Ok(Measure(GetBrowserTargets()));
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("PreviewBrowserCacheCleanup", "browser caches", ex);
                return Result<(long, Dictionary<string, long>)>.Fail(ex);
            }
        }

        #endregion

        #region Clean

        /// <summary>
        /// Deletes the files of the given targets. Every file is re-validated against its target
        /// folder and the protected-folder list right before deletion; links are never followed.
        /// Locked, read-only or protected files are skipped and counted. Empty subfolders are
        /// removed; the target folders themselves are kept.
        /// </summary>
        public static CleanupReport CleanTargets(IEnumerable<CleanupTarget> targets,
            IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            var report = new CleanupReport();
            DateTime now = DateTime.UtcNow;

            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(target.Path)) continue;
                progress?.Report($"Cleaning: {target.Path}");
                var allowed = new[] { target.Path };

                foreach (var file in SafeFileWalker.EnumerateFiles(target.Path))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (!target.Includes(file, now)) continue;
                        if (file.IsReadOnly || !Validator.IsSafeToDelete(file.FullName, allowed, out _))
                        {
                            report.FilesSkipped++;
                            continue;
                        }

                        long size = file.Length;
                        file.Delete();
                        report.FilesDeleted++;
                        report.BytesFreed += size;
                    }
                    catch
                    {
                        report.FilesSkipped++; // in use or access denied
                    }
                }

                foreach (var dir in SafeFileWalker.EnumerateDirectoriesDeepestFirst(target.Path))
                {
                    try
                    {
                        if (Validator.IsUnderDirectory(dir.FullName, target.Path) &&
                            !dir.EnumerateFileSystemInfos().Any())
                            dir.Delete(false);
                    }
                    catch { }
                }
            }
            return report;
        }

        /// <summary>Cleans temporary files (files changed in the last 24 hours are kept).</summary>
        public Task<Result<CleanupReport>> CleanTempFilesAsync(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => RunClean("CleanTempFiles", GetTempTargets(), progress, cancellationToken));
        }

        /// <summary>Cleans browser caches. Warns about running browsers (their files are locked).</summary>
        public Task<Result<CleanupReport>> CleanBrowserCachesAsync(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                var result = RunClean("CleanBrowserCaches", GetBrowserTargets(), progress, cancellationToken);
                if (result.Success)
                    result.Data!.Warnings.AddRange(GetRunningBrowserWarnings());
                return result;
            });
        }

        /// <summary>Names of supported browsers that are currently running.</summary>
        public static List<string> GetRunningBrowserWarnings()
        {
            var warnings = new List<string>();
            foreach (var (proc, name) in new[]
            {
                ("msedge", "Microsoft Edge"), ("chrome", "Google Chrome"), ("firefox", "Mozilla Firefox"),
                ("brave", "Brave"), ("opera", "Opera"), ("vivaldi", "Vivaldi")
            })
            {
                if (SystemTools.IsProcessRunning(proc))
                    warnings.Add($"{name} is running — some cache files may be locked.");
            }
            return warnings;
        }

        private static Result<CleanupReport> RunClean(string op, List<CleanupTarget> targets,
            IProgress<string>? progress, CancellationToken token)
        {
            try
            {
                Logger.Instance.Info(op, string.Join(";", targets.Select(t => t.Path)), "starting");
                var report = CleanTargets(targets, progress, token);
                Logger.Instance.Info(op, "cleanup", "success",
                    newValue: $"Deleted {report.FilesDeleted} files, freed {FormatSize(report.BytesFreed)}, skipped {report.FilesSkipped}");
                return Result<CleanupReport>.Ok(report);
            }
            catch (OperationCanceledException)
            {
                Logger.Instance.Warn(op, "cleanup", "Cancelled by user");
                return Result<CleanupReport>.Fail("Operation cancelled.");
            }
            catch (Exception ex)
            {
                Logger.Instance.Error(op, "cleanup", ex);
                return Result<CleanupReport>.Fail(ex);
            }
        }

        #endregion

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

    /// <summary>A folder to clean, with an optional minimum file age.</summary>
    public sealed record CleanupTarget(string Label, string Path, TimeSpan? MinAge)
    {
        /// <summary>Whether a file in this target is eligible for deletion.</summary>
        public bool Includes(FileInfo file, DateTime nowUtc)
        {
            if (MinAge is not TimeSpan age) return true;
            try { return nowUtc - file.LastWriteTimeUtc >= age; }
            catch { return false; }
        }
    }

    /// <summary>Result of a cleanup run, including files that could not be deleted.</summary>
    public sealed class CleanupReport
    {
        public int FilesDeleted { get; set; }
        public long BytesFreed { get; set; }
        public int FilesSkipped { get; set; }
        public List<string> Warnings { get; } = new();
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
