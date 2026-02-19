using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using KWin.Models;
using KWin.Utils;

namespace KWin.Core
{
    /// <summary>
    /// Manages listing and uninstalling Win32 desktop apps and UWP Store apps.
    /// Uses Registry for Win32, PowerShell for UWP/Store apps.
    /// All operations are logged and support progress reporting.
    /// </summary>
    public class AppManager
    {
        /// <summary>
        /// Represents an installed application (Win32 or Store).
        /// </summary>
        public class InstalledApp
        {
            public string Name { get; set; } = string.Empty;
            public string Publisher { get; set; } = string.Empty;
            public string Version { get; set; } = string.Empty;
            public string InstallDate { get; set; } = string.Empty;
            public string UninstallString { get; set; } = string.Empty;
            public string PackageFullName { get; set; } = string.Empty;
            public bool IsStoreApp { get; set; }
            public bool IsBloatware { get; set; }
            public string EstimatedSize { get; set; } = string.Empty;

            public override string ToString() => IsStoreApp
                ? $"{Name} [{Version}] (Store)"
                : $"{Name} [{Version}]";
        }

        /// <summary>
        /// Apps that MUST NEVER be uninstalled — they break Windows 11.
        /// </summary>
        private static readonly HashSet<string> ProtectedApps = new(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft.Windows.StartMenuExperienceHost",
            "Microsoft.Windows.ShellExperienceHost",
            "Microsoft.Windows.Explorer",
            "Microsoft.Windows.Search",
            "Microsoft.WindowsStore",
            "Microsoft.Windows.SecHealthUI",
            "Microsoft.SecHealthUI",
            "Microsoft.AAD.BrokerPlugin",
            "Microsoft.AccountsControl",
            "Microsoft.Windows.CloudExperienceHost",
            "Microsoft.Windows.ContentDeliveryManager",
            "Microsoft.DesktopAppInstaller",
            "Microsoft.UI.Xaml",
            "Microsoft.VCLibs",
            "Microsoft.NET.Native",
            "Microsoft.Services.Store.Engagement",
            "Microsoft.StorePurchaseApp",
            "Microsoft.WindowsStore",
            "MicrosoftWindows.Client.Core",
            "MicrosoftWindows.Client.CBS",
            "Microsoft.Windows.Apprep.ChxApp",
            "windows.immersivecontrolpanel",
            "Windows.PrintDialog",
            "Microsoft.LockApp",
            "Microsoft.Windows.OOBENetworkConnectionFlow",
            "Microsoft.Windows.OOBENetworkCaptivePortal",
            "Microsoft.Windows.ParentalControls"
        };

        /// <summary>
        /// Common bloatware apps safe to remove.
        /// </summary>
        private static readonly List<(string PackageName, string DisplayName)> BloatwareList = new()
        {
            ("Microsoft.BingNews", "Microsoft News"),
            ("Microsoft.BingWeather", "Microsoft Weather"),
            ("Microsoft.MicrosoftSolitaireCollection", "Solitaire Collection"),
            ("Microsoft.WindowsMaps", "Windows Maps"),
            ("Microsoft.People", "Microsoft People"),
            ("Microsoft.WindowsFeedbackHub", "Feedback Hub"),
            ("Microsoft.GetHelp", "Get Help"),
            ("Microsoft.Getstarted", "Tips"),
            ("Microsoft.MicrosoftOfficeHub", "Office Hub"),
            ("Microsoft.ZuneMusic", "Groove Music / Media Player (legacy)"),
            ("Microsoft.ZuneVideo", "Movies & TV"),
            ("Clipchamp.Clipchamp", "Clipchamp"),
            ("Microsoft.549981C3F5F10", "Cortana"),
            ("Microsoft.YourPhone", "Phone Link"),
            ("MicrosoftTeams", "Microsoft Teams (consumer)"),
            ("Microsoft.PowerAutomateDesktop", "Power Automate Desktop"),
            ("Microsoft.Todos", "Microsoft To Do"),
            ("Microsoft.WindowsAlarms", "Alarms & Clock"),
            ("Microsoft.WindowsSoundRecorder", "Sound Recorder"),
            ("Microsoft.MicrosoftStickyNotes", "Sticky Notes"),
            ("Microsoft.ScreenSketch", "Snipping Tool (old)"),
            ("king.com.CandyCrushSodaSaga", "Candy Crush Soda Saga"),
            ("king.com.CandyCrushFriends", "Candy Crush Friends"),
            ("SpotifyAB.SpotifyMusic", "Spotify"),
            ("Disney.37853FC22B2CE", "Disney+"),
            ("BytedancePte.Ltd.TikTok", "TikTok"),
            ("Facebook.Facebook", "Facebook"),
            ("Facebook.Instagram", "Instagram"),
            ("AmazonVideo.PrimeVideo", "Amazon Prime Video"),
        };

        /// <summary>
        /// Lists all Win32 desktop applications from the Uninstall registry keys.
        /// </summary>
        public Result<List<InstalledApp>> ListDesktopApps()
        {
            try
            {
                var apps = new List<InstalledApp>();
                string[] regPaths =
                {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                };

                foreach (var regPath in regPaths)
                {
                    using var key = Registry.LocalMachine.OpenSubKey(regPath, false);
                    if (key == null) continue;

                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var subKey = key.OpenSubKey(subKeyName, false);
                            if (subKey == null) continue;

                            string? displayName = subKey.GetValue("DisplayName")?.ToString();
                            if (string.IsNullOrWhiteSpace(displayName)) continue;

                            // Skip system components
                            var sysComponent = subKey.GetValue("SystemComponent");
                            if (sysComponent is int sc && sc == 1) continue;

                            // Skip entries without uninstall strings
                            string uninstall = subKey.GetValue("UninstallString")?.ToString() ?? "";
                            if (string.IsNullOrWhiteSpace(uninstall)) continue;

                            string version = subKey.GetValue("DisplayVersion")?.ToString() ?? "";
                            string publisher = subKey.GetValue("Publisher")?.ToString() ?? "";
                            string installDate = subKey.GetValue("InstallDate")?.ToString() ?? "";

                            long sizeKb = 0;
                            var sizeObj = subKey.GetValue("EstimatedSize");
                            if (sizeObj is int sizeInt) sizeKb = sizeInt;

                            apps.Add(new InstalledApp
                            {
                                Name = displayName,
                                Publisher = publisher,
                                Version = version,
                                InstallDate = installDate,
                                UninstallString = uninstall,
                                IsStoreApp = false,
                                EstimatedSize = sizeKb > 0 ? FormatSize(sizeKb * 1024) : ""
                            });
                        }
                        catch { /* skip unreadable entries */ }
                    }
                }

                // Also check HKCU
                using var userKey = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", false);
                if (userKey != null)
                {
                    foreach (var subKeyName in userKey.GetSubKeyNames())
                    {
                        try
                        {
                            using var subKey = userKey.OpenSubKey(subKeyName, false);
                            if (subKey == null) continue;

                            string? displayName = subKey.GetValue("DisplayName")?.ToString();
                            if (string.IsNullOrWhiteSpace(displayName)) continue;

                            string uninstall = subKey.GetValue("UninstallString")?.ToString() ?? "";
                            if (string.IsNullOrWhiteSpace(uninstall)) continue;

                            // Avoid duplicates
                            if (apps.Any(a => a.Name.Equals(displayName, StringComparison.OrdinalIgnoreCase)))
                                continue;

                            string version = subKey.GetValue("DisplayVersion")?.ToString() ?? "";
                            string publisher = subKey.GetValue("Publisher")?.ToString() ?? "";

                            apps.Add(new InstalledApp
                            {
                                Name = displayName,
                                Publisher = publisher,
                                Version = version,
                                UninstallString = uninstall,
                                IsStoreApp = false
                            });
                        }
                        catch { }
                    }
                }

                apps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                Logger.Instance.Info("ListDesktopApps", "Registry", "success",
                    newValue: $"{apps.Count} apps found");
                return Result<List<InstalledApp>>.Ok(apps);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("ListDesktopApps", "Registry", ex);
                return Result<List<InstalledApp>>.Fail(ex);
            }
        }

        /// <summary>
        /// Lists all installed Store (UWP/MSIX) apps via PowerShell Get-AppxPackage.
        /// </summary>
        public async Task<Result<List<InstalledApp>>> ListStoreAppsAsync()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -Command \"Get-AppxPackage | Select-Object Name, PackageFullName, Version, Publisher | ConvertTo-Csv -NoTypeInformation\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var process = new Process { StartInfo = psi };
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                var apps = new List<InstalledApp>();
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                // Skip header line
                for (int i = 1; i < lines.Length; i++)
                {
                    var parts = ParseCsvLine(lines[i].Trim());
                    if (parts.Length < 4) continue;

                    string name = parts[0].Trim('"');
                    string fullName = parts[1].Trim('"');
                    string version = parts[2].Trim('"');
                    string publisher = parts[3].Trim('"');

                    if (string.IsNullOrWhiteSpace(name)) continue;

                    // Skip framework packages
                    if (name.Contains(".NET", StringComparison.OrdinalIgnoreCase) &&
                        name.Contains("Native", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (name.Contains("VCLibs", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Contains("UI.Xaml", StringComparison.OrdinalIgnoreCase)) continue;

                    bool isBloatware = BloatwareList.Any(b =>
                        name.Contains(b.PackageName, StringComparison.OrdinalIgnoreCase));

                    apps.Add(new InstalledApp
                    {
                        Name = GetFriendlyAppName(name),
                        PackageFullName = fullName,
                        Version = version,
                        Publisher = CleanPublisher(publisher),
                        IsStoreApp = true,
                        IsBloatware = isBloatware
                    });
                }

                apps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                Logger.Instance.Info("ListStoreApps", "PowerShell", "success",
                    newValue: $"{apps.Count} apps found");
                return Result<List<InstalledApp>>.Ok(apps);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("ListStoreApps", "PowerShell", ex);
                return Result<List<InstalledApp>>.Fail(ex);
            }
        }

        /// <summary>
        /// Uninstalls a Win32 desktop app using its uninstall string.
        /// </summary>
        public async Task<Result<bool>> UninstallDesktopAppAsync(InstalledApp app, IProgress<string>? progress = null)
        {
            try
            {
                if (app.IsStoreApp)
                    return Result<bool>.Fail("Use UninstallStoreAppAsync for Store apps.");

                if (string.IsNullOrWhiteSpace(app.UninstallString))
                    return Result<bool>.Fail("No uninstall string available.");

                progress?.Report($"Uninstalling {app.Name}...");
                Logger.Instance.Info("UninstallDesktopApp", app.Name, "starting",
                    newValue: app.UninstallString);

                string fileName;
                string arguments;

                string uninstall = app.UninstallString.Trim();

                // Check if it's an MSI uninstall
                if (uninstall.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
                {
                    fileName = "msiexec.exe";
                    // Extract the arguments, ensure /quiet flag
                    var match = Regex.Match(uninstall, @"\{[A-F0-9\-]+\}", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        arguments = $"/x {match.Value} /passive /norestart";
                    }
                    else
                    {
                        // Use whatever arguments were provided
                        int msiIdx = uninstall.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase);
                        arguments = uninstall[(msiIdx + "msiexec.exe".Length)..].Trim();
                        if (!arguments.Contains("/passive", StringComparison.OrdinalIgnoreCase) &&
                            !arguments.Contains("/quiet", StringComparison.OrdinalIgnoreCase))
                        {
                            arguments += " /passive /norestart";
                        }
                    }
                }
                else
                {
                    // Generic uninstall — run the command via cmd
                    fileName = "cmd.exe";
                    arguments = $"/c \"{uninstall}\"";
                }

                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = fileName == "cmd.exe", // shell for unknown uninstallers
                    CreateNoWindow = fileName != "cmd.exe",
                    RedirectStandardOutput = fileName != "cmd.exe",
                    RedirectStandardError = fileName != "cmd.exe"
                };

                if (fileName == "cmd.exe")
                {
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                }

                using var process = new Process { StartInfo = psi };
                process.Start();

                // Wait up to 120 seconds for uninstall
                using var cts = new CancellationTokenSource(120000);
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(true); } catch { }
                    return Result<bool>.Fail($"Uninstall of {app.Name} timed out after 120 seconds.");
                }

                progress?.Report($"Uninstalled {app.Name}");
                Logger.Instance.Info("UninstallDesktopApp", app.Name, "success");
                return Result<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("UninstallDesktopApp", app.Name, ex);
                return Result<bool>.Fail(ex);
            }
        }

        /// <summary>
        /// Uninstalls a Store (UWP) app using PowerShell Remove-AppxPackage.
        /// </summary>
        public async Task<Result<bool>> UninstallStoreAppAsync(InstalledApp app, IProgress<string>? progress = null)
        {
            try
            {
                if (!app.IsStoreApp)
                    return Result<bool>.Fail("Use UninstallDesktopAppAsync for desktop apps.");

                if (IsProtectedApp(app.PackageFullName))
                    return Result<bool>.Fail($"'{app.Name}' is a protected system app and cannot be removed.");

                progress?.Report($"Removing {app.Name}...");
                Logger.Instance.Info("UninstallStoreApp", app.Name, "starting",
                    newValue: app.PackageFullName);

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -Command \"Remove-AppxPackage -Package '{app.PackageFullName}'\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();

                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
                {
                    Logger.Instance.Error("UninstallStoreApp", app.Name, stderr);
                    return Result<bool>.Fail($"Failed to remove {app.Name}: {stderr.Trim()}");
                }

                progress?.Report($"Removed {app.Name}");
                Logger.Instance.Info("UninstallStoreApp", app.Name, "success");
                return Result<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("UninstallStoreApp", app.Name, ex);
                return Result<bool>.Fail(ex);
            }
        }

        /// <summary>
        /// Removes a provisioned Store app for all users via DISM.
        /// Requires admin. This prevents the app from being reinstalled for new users.
        /// </summary>
        public async Task<Result<bool>> RemoveProvisionedAppAsync(string packageName, IProgress<string>? progress = null)
        {
            try
            {
                if (IsProtectedApp(packageName))
                    return Result<bool>.Fail($"'{packageName}' is a protected system app and cannot be removed.");

                progress?.Report($"Removing provisioned package: {packageName}...");
                Logger.Instance.Info("RemoveProvisionedApp", packageName, "starting");

                // First get the provisioned package name
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -Command \"Get-AppxProvisionedPackage -Online | Where-Object {{ $_.DisplayName -like '*{packageName}*' }} | Select-Object -ExpandProperty PackageName\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var findProcess = new Process { StartInfo = psi };
                findProcess.Start();
                string provisionedName = (await findProcess.StandardOutput.ReadToEndAsync()).Trim();
                await findProcess.WaitForExitAsync();

                if (string.IsNullOrWhiteSpace(provisionedName))
                {
                    // Not provisioned — just remove for current user
                    return Result<bool>.Ok(true);
                }

                // Remove provisioned package
                var removePsi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -Command \"Remove-AppxProvisionedPackage -Online -PackageName '{provisionedName}'\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var removeProcess = new Process { StartInfo = removePsi };
                removeProcess.Start();
                string removeErr = await removeProcess.StandardError.ReadToEndAsync();
                await removeProcess.WaitForExitAsync();

                if (removeProcess.ExitCode != 0 && !string.IsNullOrWhiteSpace(removeErr))
                {
                    Logger.Instance.Warn("RemoveProvisionedApp", packageName, removeErr.Trim());
                }

                progress?.Report($"Removed provisioned: {packageName}");
                Logger.Instance.Info("RemoveProvisionedApp", packageName, "success");
                return Result<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("RemoveProvisionedApp", packageName, ex);
                return Result<bool>.Fail(ex);
            }
        }

        /// <summary>
        /// Gets the list of known bloatware apps with their installed status.
        /// </summary>
        public async Task<Result<List<(string PackageName, string DisplayName, bool IsInstalled, string FullName)>>> GetBloatwareStatusAsync()
        {
            try
            {
                var storeAppsResult = await ListStoreAppsAsync();
                var storeApps = storeAppsResult.Success ? storeAppsResult.Data! : new List<InstalledApp>();

                var result = new List<(string, string, bool, string)>();
                foreach (var (pkgName, displayName) in BloatwareList)
                {
                    var installed = storeApps.FirstOrDefault(a =>
                        a.PackageFullName.Contains(pkgName, StringComparison.OrdinalIgnoreCase));
                    result.Add((pkgName, displayName, installed != null,
                        installed?.PackageFullName ?? ""));
                }

                return Result<List<(string, string, bool, string)>>.Ok(result);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("GetBloatwareStatus", "PowerShell", ex);
                return Result<List<(string, string, bool, string)>>.Fail(ex);
            }
        }

        /// <summary>
        /// One-click remove all detected bloatware. Returns count of removed / failed.
        /// </summary>
        public async Task<Result<(int Removed, int Failed, List<string> Details)>> RemoveBloatwareAsync(
            IProgress<(int current, int total, string name)>? progress = null)
        {
            try
            {
                var statusResult = await GetBloatwareStatusAsync();
                if (!statusResult.Success)
                    return Result<(int, int, List<string>)>.Fail(statusResult.ErrorMessage ?? "Failed to get bloatware list");

                var installed = statusResult.Data!.Where(b => b.IsInstalled).ToList();
                if (installed.Count == 0)
                    return Result<(int, int, List<string>)>.Ok((0, 0, new List<string> { "No bloatware found to remove." }));

                int removed = 0;
                int failed = 0;
                var details = new List<string>();

                for (int i = 0; i < installed.Count; i++)
                {
                    var (pkgName, displayName, _, fullName) = installed[i];
                    progress?.Report((i + 1, installed.Count, displayName));

                    try
                    {
                        // Remove for current user
                        var removeResult = await UninstallStoreAppAsync(new InstalledApp
                        {
                            Name = displayName,
                            PackageFullName = fullName,
                            IsStoreApp = true
                        });

                        if (removeResult.Success)
                        {
                            // Also try to remove provisioned
                            await RemoveProvisionedAppAsync(pkgName);
                            removed++;
                            details.Add($"✓ {displayName}");
                        }
                        else
                        {
                            failed++;
                            details.Add($"✗ {displayName}: {removeResult.ErrorMessage}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        details.Add($"✗ {displayName}: {ex.Message}");
                    }
                }

                Logger.Instance.Info("RemoveBloatware", "PowerShell", "complete",
                    newValue: $"Removed: {removed}, Failed: {failed}");
                return Result<(int, int, List<string>)>.Ok((removed, failed, details));
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("RemoveBloatware", "PowerShell", ex);
                return Result<(int, int, List<string>)>.Fail(ex);
            }
        }

        /// <summary>
        /// Checks if an app package is in the protected list (must not uninstall).
        /// </summary>
        public static bool IsProtectedApp(string packageName)
        {
            return ProtectedApps.Any(p =>
                packageName.Contains(p, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Gets the known bloatware apps list for display.</summary>
        public static IReadOnlyList<(string PackageName, string DisplayName)> KnownBloatware => BloatwareList;

        #region Helpers

        private static string GetFriendlyAppName(string packageName)
        {
            // Remove common prefixes for readability
            string name = packageName;
            if (name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
                name = name["Microsoft.".Length..];
            if (name.StartsWith("MicrosoftWindows.", StringComparison.OrdinalIgnoreCase))
                name = name["MicrosoftWindows.".Length..];

            // Look up display name from bloatware list
            var known = BloatwareList.FirstOrDefault(b =>
                packageName.Contains(b.PackageName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(known.DisplayName))
                return known.DisplayName;

            // Add spaces before capitals
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                    sb.Append(' ');
                sb.Append(name[i]);
            }
            return sb.ToString();
        }

        private static string CleanPublisher(string publisher)
        {
            // Remove CN= prefix from certificate-style publishers
            if (publisher.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                int comma = publisher.IndexOf(',');
                return comma > 3 ? publisher[3..comma] : publisher[3..];
            }
            return publisher;
        }

        private static string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var current = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    current.Append(c);
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result.ToArray();
        }

        private static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return $"{size:F1} {units[unit]}";
        }

        #endregion
    }
}
