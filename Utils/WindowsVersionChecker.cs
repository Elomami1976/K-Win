namespace KWin.Utils
{
    /// <summary>
    /// Validates the running Windows version to ensure compatibility with Windows 11.
    /// Uses Environment.OSVersion.Version.Build for build number detection.
    /// </summary>
    public static class WindowsVersionChecker
    {
        // Windows 11 minimum build
        private const int Windows11MinBuild = 22000;

        // Windows 11 22H2
        private const int Windows11_22H2 = 22621;

        // Windows 11 23H2
        private const int Windows11_23H2 = 22631;

        // Windows 11 24H2
        private const int Windows11_24H2 = 26100;

        /// <summary>Gets the current OS build number.</summary>
        public static int CurrentBuild => Environment.OSVersion.Version.Build;

        /// <summary>Returns true if running on Windows 11 (build 22000+).</summary>
        public static bool IsWindows11() => CurrentBuild >= Windows11MinBuild;

        /// <summary>Returns true if running on Windows 11 22H2 or later.</summary>
        public static bool Is22H2OrLater() => CurrentBuild >= Windows11_22H2;

        /// <summary>Returns true if running on Windows 11 23H2 or later.</summary>
        public static bool Is23H2OrLater() => CurrentBuild >= Windows11_23H2;

        /// <summary>Returns true if running on Windows 11 24H2 or later (Recall/AI features).</summary>
        public static bool Is24H2OrLater() => CurrentBuild >= Windows11_24H2;

        /// <summary>Gets a human-readable version string for the current Windows 11 version.</summary>
        public static string GetVersionString()
        {
            return CurrentBuild switch
            {
                >= Windows11_24H2 => $"Windows 11 24H2 (Build {CurrentBuild})",
                >= Windows11_23H2 => $"Windows 11 23H2 (Build {CurrentBuild})",
                >= Windows11_22H2 => $"Windows 11 22H2 (Build {CurrentBuild})",
                >= Windows11MinBuild => $"Windows 11 (Build {CurrentBuild})",
                _ => $"Windows (Build {CurrentBuild}) - NOT SUPPORTED"
            };
        }

        /// <summary>
        /// Validates that the current system meets minimum requirements.
        /// Returns error message if not compatible, null if OK.
        /// </summary>
        public static string? ValidateCompatibility()
        {
            if (!IsWindows11())
            {
                return $"K-win requires Windows 11 (build 22000 or later).\n" +
                       $"Current system: Build {CurrentBuild}.\n\n" +
                       "This application is designed specifically for Windows 11 optimizations.";
            }

            if (!Environment.Is64BitOperatingSystem)
            {
                return "K-win requires a 64-bit (x64) version of Windows 11.";
            }

            return null; // Compatible
        }

        /// <summary>Checks if the current user is running as Administrator.</summary>
        public static bool IsRunningAsAdmin()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Checks if the system is running on battery power (laptop detection).</summary>
        public static bool IsOnBattery()
        {
            try
            {
                return SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
            }
            catch
            {
                return false;
            }
        }
    }
}
