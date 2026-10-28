using System.Diagnostics;
using KWin.Models;
using KWin.Utils;

namespace KWin.Core
{
    /// <summary>
    /// Wraps Process.Start() for approved Windows system tools with timeout,
    /// output capture, and cancellation support. Only executes validated tools.
    /// </summary>
    public class SystemTools
    {
        /// <summary>Default timeout for all process executions (30 seconds).</summary>
        public const int DefaultTimeoutMs = 30000;

        /// <summary>
        /// Executes an approved system tool asynchronously with timeout and output capture.
        /// </summary>
        /// <param name="fileName">Tool executable name (must be in approved list).</param>
        /// <param name="arguments">Command-line arguments.</param>
        /// <param name="timeoutMs">Timeout in milliseconds (default 30000).</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>Result containing stdout output on success.</returns>
        public async Task<Result<string>> ExecuteToolAsync(
            string fileName,
            string arguments,
            int timeoutMs = DefaultTimeoutMs,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (DevMode.IsUiPreview && !IsReadOnlyQuery(fileName, arguments))
                    return Result<string>.Fail(DevMode.BlockedMessage);

                // Validate the tool is approved
                if (!Validator.IsApprovedTool(fileName))
                {
                    string error = $"Tool '{fileName}' is not in the approved list.";
                    Logger.Instance.Error("ExecuteToolAsync", fileName, error);
                    return Result<string>.Fail(error);
                }

                Logger.Instance.Info("ExecuteToolAsync", $"{fileName} {arguments}", "starting");

                var psi = new ProcessStartInfo
                {
                    FileName = ResolveSystemTool(fileName),
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = new Process { StartInfo = psi };
                process.Start();

                // Read output while the process runs; reading only after exit can deadlock
                // when a tool (e.g. sfc) writes more than the pipe buffer holds.
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                // Create a combined cancellation token with timeout
                using var timeoutCts = new CancellationTokenSource(timeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, timeoutCts.Token);

                try
                {
                    await process.WaitForExitAsync(linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Kill the process if it didn't exit in time
                    try { process.Kill(true); } catch { }

                    if (timeoutCts.IsCancellationRequested)
                    {
                        string timeoutError = $"Process timeout after {timeoutMs}ms";
                        Logger.Instance.Error("ExecuteToolAsync", $"{fileName} {arguments}", timeoutError);
                        return Result<string>.Fail(timeoutError);
                    }

                    Logger.Instance.Error("ExecuteToolAsync", $"{fileName} {arguments}", "Cancelled by user");
                    return Result<string>.Fail("Operation cancelled.");
                }

                string stdout = await stdoutTask;
                string stderr = await stderrTask;

                if (process.ExitCode == 0)
                {
                    Logger.Instance.Info("ExecuteToolAsync", $"{fileName} {arguments}", "success");
                    return Result<string>.Ok(stdout);
                }

                string exitError = $"Exit code {process.ExitCode}: {stderr}".Trim();
                Logger.Instance.Error("ExecuteToolAsync", $"{fileName} {arguments}", exitError);
                return Result<string>.Fail(exitError);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("ExecuteToolAsync", $"{fileName} {arguments}", ex);
                return Result<string>.Fail(ex);
            }
        }

        /// <summary>
        /// Executes a tool that may open a UI window (like cleanmgr or wsreset).
        /// Uses shell execution and waits for exit with timeout.
        /// </summary>
        public async Task<Result<bool>> ExecuteToolWithUIAsync(
            string fileName,
            string arguments = "",
            int timeoutMs = DefaultTimeoutMs,
            CancellationToken cancellationToken = default)
        {
            try
            {
                DevMode.ThrowIfReadOnly(fileName);
                if (!Validator.IsApprovedTool(fileName))
                    return Result<bool>.Fail($"Tool '{fileName}' is not in the approved list.");

                Logger.Instance.Info("ExecuteToolWithUIAsync", $"{fileName} {arguments}", "starting");

                var psi = new ProcessStartInfo
                {
                    FileName = ResolveSystemTool(fileName),
                    Arguments = arguments,
                    UseShellExecute = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                    return Result<bool>.Fail($"Failed to start {fileName}");

                using var timeoutCts = new CancellationTokenSource(timeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, timeoutCts.Token);

                try
                {
                    await process.WaitForExitAsync(linkedCts.Token);
                    Logger.Instance.Info("ExecuteToolWithUIAsync", $"{fileName} {arguments}", "success");
                    return Result<bool>.Ok(true);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(true); } catch { }

                    if (timeoutCts.IsCancellationRequested)
                    {
                        Logger.Instance.Warn("ExecuteToolWithUIAsync", $"{fileName} {arguments}",
                            $"Process exceeded {timeoutMs}ms timeout, killed.");
                        return Result<bool>.Fail($"Process timeout after {timeoutMs}ms");
                    }

                    return Result<bool>.Fail("Operation cancelled.");
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("ExecuteToolWithUIAsync", $"{fileName} {arguments}", ex);
                return Result<bool>.Fail(ex);
            }
        }

        /// <summary>
        /// Opens a Windows Settings page using ms-settings: URI.
        /// </summary>
        /// <param name="settingsUri">Settings URI (e.g., "ms-settings:windowsdefender").</param>
        public Result<bool> OpenWindowsSettings(string settingsUri)
        {
            try
            {
                if (!settingsUri.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
                    return Result<bool>.Fail("Invalid settings URI. Must start with 'ms-settings:'");

                Process.Start(new ProcessStartInfo
                {
                    FileName = settingsUri,
                    UseShellExecute = true
                });

                Logger.Instance.Info("OpenWindowsSettings", settingsUri, "success");
                return Result<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("OpenWindowsSettings", settingsUri, ex);
                return Result<bool>.Fail(ex);
            }
        }

        /// <summary>
        /// Gets the currently active power plan GUID using powercfg.
        /// </summary>
        public async Task<Result<string>> GetActivePowerPlanAsync()
        {
            try
            {
                var result = await ExecuteToolAsync("powercfg.exe", "/getactivescheme");
                if (!result.Success) return Result<string>.Fail(result.ErrorMessage ?? "Failed");

                // Parse GUID from output like: "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)"
                string output = result.Data ?? "";
                int guidStart = output.IndexOf(':');
                if (guidStart >= 0)
                {
                    string afterColon = output[(guidStart + 1)..].Trim();
                    int guidEnd = afterColon.IndexOf(' ');
                    if (guidEnd < 0) guidEnd = afterColon.Length;
                    string guid = afterColon[..guidEnd].Trim();
                    return Result<string>.Ok(guid);
                }

                return Result<string>.Fail("Could not parse active power plan GUID.");
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("GetActivePowerPlan", "powercfg", ex);
                return Result<string>.Fail(ex);
            }
        }

        /// <summary>
        /// Lists all available power plans.
        /// </summary>
        public async Task<Result<List<(string Guid, string Name, bool Active)>>> ListPowerPlansAsync()
        {
            try
            {
                var result = await ExecuteToolAsync("powercfg.exe", "/list");
                if (!result.Success)
                    return Result<List<(string, string, bool)>>.Fail(result.ErrorMessage ?? "Failed");

                var plans = new List<(string Guid, string Name, bool Active)>();
                string output = result.Data ?? "";

                foreach (string line in output.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (!trimmed.Contains("GUID:", StringComparison.OrdinalIgnoreCase))
                        continue;

                    int guidIdx = trimmed.IndexOf("GUID:", StringComparison.OrdinalIgnoreCase);
                    if (guidIdx < 0) continue;

                    string afterGuid = trimmed[(guidIdx + 5)..].Trim();
                    int spaceIdx = afterGuid.IndexOf(' ');
                    if (spaceIdx < 0) continue;

                    string guid = afterGuid[..spaceIdx].Trim();

                    // Extract name between parentheses
                    int nameStart = afterGuid.IndexOf('(');
                    int nameEnd = afterGuid.IndexOf(')');
                    string name = nameStart >= 0 && nameEnd > nameStart
                        ? afterGuid[(nameStart + 1)..nameEnd]
                        : "Unknown";

                    bool active = trimmed.Contains('*');
                    plans.Add((guid, name, active));
                }

                return Result<List<(string, string, bool)>>.Ok(plans);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("ListPowerPlans", "powercfg", ex);
                return Result<List<(string, string, bool)>>.Fail(ex);
            }
        }

        /// <summary>
        /// Sets the active power plan by GUID.
        /// </summary>
        public async Task<Result<string>> SetPowerPlanAsync(string guid)
        {
            if (!Validator.IsValidPowerPlanGuid(guid))
                return Result<string>.Fail($"Invalid power plan GUID: {guid}");

            return await ExecuteToolAsync("powercfg.exe", $"/setactive {guid}");
        }

        /// <summary>
        /// Creates the Ultimate Performance power plan if it doesn't already exist.
        /// Uses powercfg /duplicatescheme to create from the hidden template.
        /// </summary>
        public async Task<Result<string>> CreateUltimatePerformancePlanAsync()
        {
            try
            {
                // Check if Ultimate Performance already exists
                var plans = await ListPowerPlansAsync();
                if (plans.Success && plans.Data != null)
                {
                    var existing = plans.Data.FirstOrDefault(p =>
                        p.Guid.Equals("e9a42b02-d5df-448d-aa00-03f14749eb61", StringComparison.OrdinalIgnoreCase));
                    if (existing != default)
                        return Result<string>.Ok("Ultimate Performance plan already exists.");
                }

                // Create by duplicating the hidden scheme
                var result = await ExecuteToolAsync("powercfg.exe",
                    "/duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61");

                if (result.Success)
                {
                    Logger.Instance.Info("CreateUltimatePerformancePlan", "powercfg", "success");
                    return Result<string>.Ok("Ultimate Performance plan created successfully.");
                }

                return Result<string>.Fail(result.ErrorMessage ?? "Failed to create Ultimate Performance plan.");
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("CreateUltimatePerformancePlan", "powercfg", ex);
                return Result<string>.Fail(ex);
            }
        }

        /// <summary>
        /// Checks if a Windows service exists and gets its current status.
        /// </summary>
        public static Result<(string Status, string StartType)> GetServiceInfo(string serviceName)
        {
            try
            {
                using var sc = new System.ServiceProcess.ServiceController(serviceName);
                string status = sc.Status.ToString();
                string startType = sc.StartType.ToString();
                return Result<(string, string)>.Ok((status, startType));
            }
            catch (Exception ex)
            {
                return Result<(string, string)>.Fail(ex);
            }
        }

        /// <summary>
        /// Stops a Windows service (only non-critical services).
        /// The start type is changed separately through RegistryHelper so it is backed up and reversible.
        /// </summary>
        public static async Task<Result<bool>> StopServiceAsync(string serviceName)
        {
            try
            {
                DevMode.ThrowIfReadOnly("stop service");
                if (Validator.IsCriticalService(serviceName))
                    return Result<bool>.Fail($"Service '{serviceName}' is marked as critical and cannot be stopped.");

                using var sc = new System.ServiceProcess.ServiceController(serviceName);
                if (sc.Status != System.ServiceProcess.ServiceControllerStatus.Stopped)
                {
                    sc.Stop();
                    await Task.Run(() => sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped,
                        TimeSpan.FromSeconds(20)));
                }
                Logger.Instance.Info("StopService", serviceName, "success");
                return Result<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("StopService", serviceName, ex);
                return Result<bool>.Fail(ex);
            }
        }

        /// <summary>Starts a Windows service and waits until it is running.</summary>
        public static async Task<Result<bool>> StartServiceAsync(string serviceName)
        {
            try
            {
                DevMode.ThrowIfReadOnly("start service");
                using var sc = new System.ServiceProcess.ServiceController(serviceName);
                if (sc.Status != System.ServiceProcess.ServiceControllerStatus.Running)
                {
                    sc.Start();
                    await Task.Run(() => sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running,
                        TimeSpan.FromSeconds(20)));
                }
                Logger.Instance.Info("StartService", serviceName, "success");
                return Result<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("StartService", serviceName, ex);
                return Result<bool>.Fail(ex);
            }
        }

        /// <summary>True if the service exists and is running.</summary>
        public static bool IsServiceRunning(string serviceName)
        {
            try
            {
                using var sc = new System.ServiceProcess.ServiceController(serviceName);
                return sc.Status == System.ServiceProcess.ServiceControllerStatus.Running;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Tool calls that only read information (allowed in the development UI preview).</summary>
        private static bool IsReadOnlyQuery(string fileName, string arguments)
        {
            string name = Path.GetFileName(fileName).ToLowerInvariant();
            string args = arguments.Trim().ToLowerInvariant();
            return name == "powercfg.exe" && (args == "/list" || args == "/getactivescheme");
        }

        /// <summary>
        /// Maps an approved tool name to its full path in System32 so a same-named program
        /// in the current folder or PATH can never be started instead.
        /// </summary>
        public static string ResolveSystemTool(string fileName)
        {
            string name = Path.GetFileName(fileName);
            string candidate = Path.Combine(Environment.SystemDirectory, name);
            if (File.Exists(candidate)) return candidate;
            string winCandidate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), name);
            return File.Exists(winCandidate) ? winCandidate : candidate;
        }

        /// <summary>
        /// Checks if a specific process is currently running.
        /// </summary>
        public static bool IsProcessRunning(string processName)
        {
            try
            {
                return Process.GetProcessesByName(
                    Path.GetFileNameWithoutExtension(processName)).Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
