using Microsoft.Win32;

namespace KWin.Utils
{
    /// <summary>
    /// Input validation utility for registry paths, file paths, and other user inputs.
    /// Ensures safety by validating all inputs before system operations.
    /// </summary>
    public static class Validator
    {
        /// <summary>Known safe registry root keys for K-win operations.</summary>
        private static readonly HashSet<string> AllowedRegistryRoots = new(StringComparer.OrdinalIgnoreCase)
        {
            "HKEY_CURRENT_USER",
            "HKEY_LOCAL_MACHINE",
            "HKCU",
            "HKLM"
        };

        /// <summary>Critical Windows services that must not be disabled.</summary>
        private static readonly HashSet<string> CriticalServices = new(StringComparer.OrdinalIgnoreCase)
        {
            "wuauserv",       // Windows Update
            "WinDefend",      // Windows Defender
            "Winmgmt",        // WMI
            "RpcSs",          // RPC
            "PlugPlay",       // Plug and Play
            "EventLog",       // Event Log
            "Dhcp",           // DHCP Client
            "Dnscache",       // DNS Client
            "nsi",            // Network Store Interface
            "LanmanWorkstation", // Workstation
            "CryptSvc",       // Cryptographic Services
            "TrustedInstaller", // Windows Modules Installer
            "BFE",            // Base Filtering Engine
            "mpssvc",         // Windows Firewall
            "Schedule",       // Task Scheduler
            "SENS",           // System Event Notification
            "Power",          // Power
            "ProfSvc",        // User Profile Service
            "SamSs",          // Security Accounts Manager
            "lsass"           // Local Security Authority
        };

        /// <summary>
        /// Validates a registry key path is safe to modify.
        /// </summary>
        public static bool IsValidRegistryPath(string keyPath, out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(keyPath))
            {
                error = "Registry path cannot be empty.";
                return false;
            }

            // Check root key
            string root = keyPath.Split('\\')[0];
            if (!AllowedRegistryRoots.Contains(root))
            {
                error = $"Registry root '{root}' is not in the allowed list.";
                return false;
            }

            // Block dangerous paths
            string upper = keyPath.ToUpperInvariant();
            if (upper.Contains(@"\SYSTEM\CURRENTCONTROLSET\CONTROL\LSA") ||
                upper.Contains(@"\SYSTEM\CURRENTCONTROLSET\SERVICES\LSASS") ||
                upper.Contains(@"\SYSTEM\CURRENTCONTROLSET\CONTROL\SECUREBOOT") ||
                upper.Contains(@"\SYSTEM\CURRENTCONTROLSET\CONTROL\CI") ||
                upper.Contains(@"\BCD00000000"))
            {
                error = "Modifying this registry path is blocked for safety.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if a service name is critical and should not be disabled.
        /// </summary>
        public static bool IsCriticalService(string serviceName)
        {
            return CriticalServices.Contains(serviceName);
        }

        /// <summary>
        /// True if <paramref name="path"/> is strictly inside <paramref name="directory"/>
        /// (compares whole path segments, so C:\Temp2 is NOT inside C:\Temp).
        /// </summary>
        public static bool IsUnderDirectory(string path, string directory)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
                return false;
            try
            {
                string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                string dir = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return full.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Folders whose contents cleanup must never delete, whatever the allowed roots say.</summary>
        public static IReadOnlyList<string> GetProtectedDirectories() => _protectedDirs.Value;

        private static readonly Lazy<IReadOnlyList<string>> _protectedDirs = new(BuildProtectedDirectories);

        private static IReadOnlyList<string> BuildProtectedDirectories()
        {
            var list = new List<string>();
            void Add(Environment.SpecialFolder f)
            {
                try
                {
                    string p = Environment.GetFolderPath(f);
                    if (!string.IsNullOrEmpty(p)) list.Add(p);
                }
                catch { }
            }

            Add(Environment.SpecialFolder.Desktop);
            Add(Environment.SpecialFolder.MyDocuments);
            Add(Environment.SpecialFolder.MyPictures);
            Add(Environment.SpecialFolder.MyMusic);
            Add(Environment.SpecialFolder.MyVideos);
            Add(Environment.SpecialFolder.Favorites);
            Add(Environment.SpecialFolder.ProgramFiles);
            Add(Environment.SpecialFolder.ProgramFilesX86);
            Add(Environment.SpecialFolder.System);
            Add(Environment.SpecialFolder.SystemX86);

            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile))
            {
                list.Add(Path.Combine(profile, "Downloads"));
                list.Add(Path.Combine(profile, "OneDrive"));
            }
            foreach (var env in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
            {
                string? od = Environment.GetEnvironmentVariable(env);
                if (!string.IsNullOrEmpty(od)) list.Add(od);
            }

            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(win))
            {
                list.Add(Path.Combine(win, "WinSxS"));
                list.Add(Path.Combine(win, "SysWOW64"));
            }
            return list;
        }

        /// <summary>
        /// Validates that a file may be deleted by cleanup:
        ///  - it must be strictly inside one of <paramref name="allowedRoots"/>,
        ///  - it must not be inside a protected folder (Documents, Desktop, Downloads, Program Files, System32, ...),
        ///  - it must not be a junction or symbolic link, and its folder must not be one either.
        /// </summary>
        public static bool IsSafeToDelete(string filePath, IEnumerable<string> allowedRoots, out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                error = "File path cannot be empty.";
                return false;
            }

            string fullPath;
            try { fullPath = Path.GetFullPath(filePath); }
            catch { error = "Invalid file path."; return false; }

            if (!allowedRoots.Any(r => IsUnderDirectory(fullPath, r)))
            {
                error = "File is outside the folders selected for cleanup.";
                return false;
            }

            if (GetProtectedDirectories().Any(p =>
                    IsUnderDirectory(fullPath, p) || string.Equals(Path.GetFullPath(p).TrimEnd('\\'), fullPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
            {
                error = "File is inside a protected folder.";
                return false;
            }

            try
            {
                var fi = new FileInfo(fullPath);
                if (fi.Exists && fi.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    error = "File is a link (reparse point).";
                    return false;
                }
                var parent = fi.Directory;
                if (parent != null && parent.Exists && parent.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    error = "File's folder is a link (reparse point).";
                    return false;
                }
            }
            catch
            {
                error = "Could not read file attributes.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates a power plan GUID is one of the known safe GUIDs.
        /// </summary>
        public static bool IsValidPowerPlanGuid(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid)) return false;

            return guid.ToLowerInvariant() switch
            {
                "381b4222-f694-41f0-9685-ff5bb260df2e" => true, // Balanced
                "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" => true, // High Performance
                "e9a42b02-d5df-448d-aa00-03f14749eb61" => true, // Ultimate Performance
                "a1841308-3541-4fab-bc81-f71556f20b4a" => true, // Power Saver
                _ => Guid.TryParse(guid, out _) // Accept dynamically discovered custom plans
            };
        }

        /// <summary>
        /// Gets the display name for a power plan GUID.
        /// </summary>
        public static string GetPowerPlanName(string guid)
        {
            return guid.ToLowerInvariant() switch
            {
                "381b4222-f694-41f0-9685-ff5bb260df2e" => "Balanced",
                "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" => "High Performance",
                "e9a42b02-d5df-448d-aa00-03f14749eb61" => "Ultimate Performance",
                "a1841308-3541-4fab-bc81-f71556f20b4a" => "Power Saver",
                _ => "Custom Plan"
            };
        }

        /// <summary>
        /// Validates that a process name is in the approved tools list.
        /// </summary>
        public static bool IsApprovedTool(string fileName)
        {
            string name = Path.GetFileName(fileName).ToLowerInvariant();
            return name switch
            {
                "powercfg.exe" => true,
                "cleanmgr.exe" => true,
                "sfc.exe" => true,
                "dism.exe" => true,
                "wsreset.exe" => true,
                "systempropertiesperformance.exe" => true,
                "reg.exe" => true,
                "netsh.exe" => true,
                "ipconfig.exe" => true,
                // cmd.exe and powershell.exe were removed in 1.5: no shell is ever run through this path.
                _ => false
            };
        }
    }
}
