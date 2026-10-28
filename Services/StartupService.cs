using System.Diagnostics;
using KWin.Core;
using KWin.Models;
using KWin.Utils;
using Microsoft.Win32;

namespace KWin.Services
{
    public enum StartupSource
    {
        /// <summary>HKCU\...\Run (current user)</summary>
        RegistryUser,
        /// <summary>HKLM\...\Run, 64-bit view (all users)</summary>
        RegistryMachine,
        /// <summary>HKLM\SOFTWARE\WOW6432Node\...\Run, 32-bit view (all users)</summary>
        RegistryMachine32,
        /// <summary>%AppData%\Microsoft\Windows\Start Menu\Programs\Startup</summary>
        FolderUser,
        /// <summary>%ProgramData%\Microsoft\Windows\Start Menu\Programs\StartUp</summary>
        FolderCommon
    }

    public sealed class StartupEntry
    {
        public required string Name { get; init; }
        public required string Command { get; init; }
        public required StartupSource Source { get; init; }
        /// <summary>Registry key or folder the entry comes from.</summary>
        public required string Location { get; init; }
        /// <summary>Program file, when it can be determined from the command or shortcut.</summary>
        public string? ExecutablePath { get; set; }
        public string? Publisher { get; set; }
        public bool Enabled { get; init; }
        /// <summary>False when the program file does not exist (a leftover entry).</summary>
        public bool TargetExists { get; set; } = true;
        /// <summary>StartupApproved key + value name that hold the enabled/disabled flag.</summary>
        public required string ApprovedKey { get; init; }
        public required string ApprovedValueName { get; init; }

        public bool IsMachineWide => Source is StartupSource.RegistryMachine or StartupSource.RegistryMachine32 or StartupSource.FolderCommon;
    }

    /// <summary>Where startup entries live. <see cref="Default"/> is the real Windows layout; tests use their own.</summary>
    public sealed class StartupLocations
    {
        public required string UserRun { get; init; }
        public required string MachineRun { get; init; }
        public required string MachineRun32 { get; init; }
        public required string UserApproved { get; init; }
        public required string MachineApproved { get; init; }
        public required string UserFolder { get; init; }
        public required string CommonFolder { get; init; }

        public static StartupLocations Default { get; } = new()
        {
            UserRun = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
            MachineRun = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            MachineRun32 = @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
            UserApproved = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved",
            MachineApproved = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved",
            UserFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            CommonFolder = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
        };
    }

    /// <summary>
    /// Lists startup programs and enables/disables them the way Task Manager does: by writing the
    /// StartupApproved flag. The Run value or shortcut itself is never changed or deleted, so the
    /// original configuration is always kept. Flag writes go through RegistryHelper (backed up,
    /// verified, undoable).
    /// </summary>
    public sealed class StartupService
    {
        private readonly RegistryHelper _registry;
        private readonly StartupLocations _loc;

        public StartupService(RegistryHelper registry, StartupLocations? locations = null)
        {
            _registry = registry;
            _loc = locations ?? StartupLocations.Default;
        }

        /// <summary>Reads all startup entries. Unreadable locations are skipped.</summary>
        public List<StartupEntry> Discover()
        {
            var list = new List<StartupEntry>();
            AddRegistry(list, _loc.UserRun, StartupSource.RegistryUser, _loc.UserApproved + @"\Run");
            AddRegistry(list, _loc.MachineRun, StartupSource.RegistryMachine, _loc.MachineApproved + @"\Run");
            AddRegistry(list, _loc.MachineRun32, StartupSource.RegistryMachine32, _loc.MachineApproved + @"\Run32");
            AddFolder(list, _loc.UserFolder, StartupSource.FolderUser, _loc.UserApproved + @"\StartupFolder");
            AddFolder(list, _loc.CommonFolder, StartupSource.FolderCommon, _loc.MachineApproved + @"\StartupFolder");
            return list;
        }

        private void AddRegistry(List<StartupEntry> list, string runKey, StartupSource source, string approvedKey)
        {
            var names = _registry.GetValueNames(runKey);
            if (!names.Success || names.Data == null) return;
            foreach (var name in names.Data)
            {
                if (string.IsNullOrWhiteSpace(name)) continue; // the unnamed default value is not a startup entry
                var value = _registry.GetValue(runKey, name);
                if (!value.Success || value.Data is not string command) continue;
                list.Add(new StartupEntry
                {
                    Name = name,
                    Command = command,
                    Source = source,
                    Location = runKey,
                    ApprovedKey = approvedKey,
                    ApprovedValueName = name,
                    Enabled = IsApproved(approvedKey, name),
                    ExecutablePath = ExtractExecutable(command)
                });
            }
        }

        private void AddFolder(List<StartupEntry> list, string folder, StartupSource source, string approvedKey)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(folder).ToList(); }
            catch { return; }

            foreach (var file in files)
            {
                string fileName = Path.GetFileName(file);
                if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new StartupEntry
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Command = file,
                    Source = source,
                    Location = folder,
                    ApprovedKey = approvedKey,
                    ApprovedValueName = fileName,
                    Enabled = IsApproved(approvedKey, fileName),
                    ExecutablePath = file
                });
            }
        }

        private bool IsApproved(string approvedKey, string valueName)
        {
            var v = _registry.GetValue(approvedKey, valueName);
            return ParseApproved(v.Success ? v.Data as byte[] : null);
        }

        /// <summary>
        /// StartupApproved value: missing = enabled (Windows default). First byte even (02, 06) =
        /// enabled, odd (03, 07) = disabled - the format Task Manager writes.
        /// </summary>
        public static bool ParseApproved(byte[]? data) => data == null || data.Length == 0 || (data[0] & 1) == 0;

        /// <summary>The 12-byte StartupApproved value: flag + disable timestamp (FILETIME).</summary>
        public static byte[] BuildApproved(bool enabled, DateTime utcNow)
        {
            var data = new byte[12];
            data[0] = enabled ? (byte)0x02 : (byte)0x03;
            if (!enabled)
                BitConverter.GetBytes(utcNow.ToFileTimeUtc()).CopyTo(data, 4);
            return data;
        }

        /// <summary>Enables or disables an entry by writing its StartupApproved flag only.</summary>
        public Result<bool> SetEnabled(StartupEntry entry, bool enabled)
        {
            try
            {
                DevMode.ThrowIfReadOnly("startup change");
                var r = _registry.SetValueWithBackup(entry.ApprovedKey, entry.ApprovedValueName,
                    BuildApproved(enabled, DateTime.UtcNow), RegistryValueKind.Binary);
                if (!r.Success) return Result<bool>.Fail(r.ErrorMessage ?? "Write failed");

                if (IsApproved(entry.ApprovedKey, entry.ApprovedValueName) != enabled)
                    return Result<bool>.Fail("Verification failed: Windows does not report the new state.");
                return Result<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                return Result<bool>.Fail(ex);
            }
        }

        /// <summary>
        /// Fills in publisher and "file exists" (reads file version info; resolves .lnk shortcuts).
        /// Slow for many entries, so call it on a background thread.
        /// </summary>
        public static void FillDetails(StartupEntry e)
        {
            try
            {
                string? path = e.ExecutablePath;
                if (path != null && path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    path = ResolveShortcut(path) ?? path;
                e.ExecutablePath = path;
                if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path)) { e.TargetExists = path != null; return; }
                e.TargetExists = File.Exists(path);
                if (e.TargetExists)
                {
                    var info = FileVersionInfo.GetVersionInfo(path);
                    e.Publisher = string.IsNullOrWhiteSpace(info.CompanyName) ? null : info.CompanyName.Trim();
                }
            }
            catch
            {
                // leave publisher unknown
            }
        }

        private static string? ResolveShortcut(string lnk)
        {
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return null;
                dynamic shell = Activator.CreateInstance(type)!;
                try
                {
                    dynamic shortcut = shell.CreateShortcut(lnk);
                    string target = shortcut.TargetPath;
                    return string.IsNullOrWhiteSpace(target) ? null : target;
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Best-effort program path from a Run command (for display only - nothing is executed).</summary>
        public static string? ExtractExecutable(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            string s = Environment.ExpandEnvironmentVariables(command.Trim());
            if (s.StartsWith('"'))
            {
                int end = s.IndexOf('"', 1);
                return end > 1 ? s[1..end] : null;
            }
            int exe = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe > 0) return s[..(exe + 4)];
            int space = s.IndexOf(' ');
            return space > 0 ? s[..space] : s;
        }
    }
}
