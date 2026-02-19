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
        /// Validates a file path is safe to delete (not in protected directories).
        /// </summary>
        public static bool IsSafeToDelete(string filePath, out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                error = "File path cannot be empty.";
                return false;
            }

            string fullPath = Path.GetFullPath(filePath).ToUpperInvariant();
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToUpperInvariant();
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).ToUpperInvariant();
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).ToUpperInvariant();

            // Block System32, SysWOW64, WinSxS
            if (fullPath.Contains(Path.Combine(winDir, "SYSTEM32")) ||
                fullPath.Contains(Path.Combine(winDir, "SYSWOW64")) ||
                fullPath.Contains(Path.Combine(winDir, "WINSXS")))
            {
                error = "Deleting files in protected Windows directories is forbidden.";
                return false;
            }

            // Block Program Files root (but allow cache subdirectories)
            if (fullPath.StartsWith(programFiles + "\\") || fullPath.StartsWith(programFilesX86 + "\\"))
            {
                // Only allow known cache paths
                if (!fullPath.Contains("\\CACHE") && !fullPath.Contains("\\TEMP"))
                {
                    error = "Deleting files in Program Files is restricted. Only cache/temp subdirectories allowed.";
                    return false;
                }
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
                "msiexec.exe" => true,
                "powershell.exe" => true,
                "cmd.exe" => true,
                _ => false
            };
        }
    }
}
