using Microsoft.Win32;
using KWin.Core;
using KWin.Models;
using KWin.Services;
using KWin.Themes;
using KWin.UI.Pages;
using KWin.Utils;
using static KWin.Utils.Loc;

namespace KWin.UI
{
    /// <summary>
    /// Main application window. The 1.5 shell (sidebar, top bar, pages, status bar) is built in
    /// MainForm.Shell.cs; this file holds the feature pages and operations carried over from 1.0.
    /// </summary>
    public partial class MainForm : Form
    {
        // Core services
        private readonly SafetyEngine _safety;
        private readonly RegistryHelper _registry;
        private readonly SystemTools _systemTools;
        private readonly CleanupManager _cleanup;
        private readonly AppManager _appManager;
        private readonly OperationHistoryService _history;

        // Operation status tracking (an operation fails if any status was reported as an error)
        private bool _statusErrorDuringOp;
        private bool _operationHasIrreversibleSteps;
        private string? _lastErrorStatus;
        private readonly ToolTip _undoToolTip = new();

        // UI components shared by the shell and the 1.0 pages
        private readonly ProgressBar _progressBar;
        private readonly Label _statusLabel;
        private readonly Button _undoButton;
        private readonly Button _logViewerButton;

        // Theme
        private bool _isDarkMode;
        private bool _autoRestorePoint = true;

        // Colors
        private static readonly Color AccentBlue = Color.FromArgb(0, 120, 212);
        private static readonly Color DarkBg = Color.FromArgb(32, 32, 32);
        private static readonly Color DarkSurface = Color.FromArgb(43, 43, 43);
        private static readonly Color DarkText = Color.FromArgb(230, 230, 230);
        private static readonly Color LightBg = Color.FromArgb(243, 243, 243);
        private static readonly Color LightSurface = Color.White;
        private static readonly Color LightText = Color.FromArgb(30, 30, 30);

        public MainForm(int? initialTab = null)
        {
            // Initialize core services
            _registry = new RegistryHelper();
            _safety = new SafetyEngine(_registry);
            _history = new OperationHistoryService();
            _systemTools = new SystemTools();
            _cleanup = new CleanupManager(_systemTools);
            _appManager = new AppManager();

            // Saved preferences (theme, restore point option)
            _settingsService = new SettingsService();
            _autoRestorePoint = _settingsService.Current.AutoRestorePoint;
            _isDarkMode = _settingsService.Current.IsDark;
            ThemeManager.SetTheme(_isDarkMode);

            // Controls shared by the shell (status bar / top bar) and the 1.0 pages
            _progressBar = new ProgressBar { Style = ProgressBarStyle.Continuous };
            _statusLabel = new Label { Text = Get("app.ready"), UseMnemonic = false };
            _undoButton = new Button { Text = Get("app.undo"), Enabled = false };
            _undoButton.Click += UndoButton_Click;
            _logViewerButton = new Button { Text = Get("app.logs") };
            _logViewerButton.Click += LogViewerButton_Click;

            // 1.5 window shell (sidebar, top bar, pages, status bar)
            BuildShell(initialTab);
            ApplyTheme();

            // Undo history is loaded from disk
            UpdateUndoButton();

            // Log startup
            Logger.Instance.Info("Application", "MainForm", "started",
                newValue: WindowsVersionChecker.GetVersionString());
        }



        #region Performance Operations

        private async Task ApplyPowerPlanAsync(string guid)
        {
            var changes = new List<string>
            {
                Get("perf.op.set_plan_to", Validator.GetPowerPlanName(guid), guid)
            };

            if (WindowsVersionChecker.IsOnBattery() && guid != "381b4222-f694-41f0-9685-ff5bb260df2e")
            {
                changes.Add(Get("perf.op.battery_warning"));
            }

            if (!PreviewDialog.ShowPreview(this, Get("perf.op.change_power_plan"), changes))
                return;

            await ExecuteUserLevelAsync(Get("perf.op.change_power_plan"), async () =>
            {
                // Save current plan for undo
                var currentPlan = await _systemTools.GetActivePowerPlanAsync();
                string previousGuid = currentPlan.Success ? currentPlan.Data! : "";

                var result = await _systemTools.SetPowerPlanAsync(guid);
                if (result.Success)
                {
                    if (!string.IsNullOrEmpty(previousGuid))
                    {
                        _safety.PushUndoAction(new UndoAction
                        {
                            Description = Get("perf.op.revert_power_plan", Validator.GetPowerPlanName(previousGuid)),
                            UndoDelegate = async () =>
                            {
                                var r = await _systemTools.SetPowerPlanAsync(previousGuid);
                                return r.Success ? Result<bool>.Ok(true) : Result<bool>.Fail(r.ErrorMessage ?? "Failed");
                            }
                        });
                        UpdateUndoButton();
                    }
                    SetStatus(Get("perf.op.power_plan_set", Validator.GetPowerPlanName(guid)), false);
                }
                else
                {
                    SetStatus(Get("failed", result.ErrorMessage ?? ""), true);
                }
            });
        }

        private async Task OneClickPerformanceBoostAsync(bool disableTransparency)
        {
            var changes = new List<string>
            {
                Get("perf.op.one_click_1"),
                Get("perf.op.one_click_2"),
            };
            if (disableTransparency)
                changes.Add(Get("perf.op.one_click_3"));
            changes.Add(Get("perf.op.one_click_rp"));

            if (!PreviewDialog.ShowPreview(this, Get("perf.op.one_click_title"), changes,
                WindowsVersionChecker.IsOnBattery()
                    ? Get("perf.op.battery_boost_warning")
                    : null))
                return;

            await ExecuteUserLevelAsync(Get("perf.op.one_click_title"), async () =>
            {
                var backups = new List<string>();

                // 1. Power Plan
                var currentPlan = await _systemTools.GetActivePowerPlanAsync();
                string previousGuid = currentPlan.Success ? currentPlan.Data! : "";
                var planResult = await _systemTools.SetPowerPlanAsync("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
                if (!planResult.Success)
                    SetStatus(Get("failed", planResult.ErrorMessage ?? "powercfg"), true);

                // 2. Visual effects
                var r1 = _registry.SetValueWithBackup(
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
                    "VisualFXSetting", 2, RegistryValueKind.DWord);
                if (r1.Success) backups.Add(r1.Data!);

                // 3. Transparency
                if (disableTransparency)
                {
                    var r2 = _registry.SetValueWithBackup(
                        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                        "EnableTransparency", 0, RegistryValueKind.DWord);
                    if (r2.Success) backups.Add(r2.Data!);
                }

                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("perf.op.revert_boost"),
                    BackupFiles = backups,
                    // Registry values are reverted precisely by SafetyEngine; this only restores the power plan.
                    UndoDelegate = async () =>
                    {
                        if (string.IsNullOrEmpty(previousGuid)) return Result<bool>.Ok(true);
                        var r = await _systemTools.SetPowerPlanAsync(previousGuid);
                        return r.Success ? Result<bool>.Ok(true) : Result<bool>.Fail(r.ErrorMessage ?? "powercfg failed");
                    }
                });
                UpdateUndoButton();

                SetStatus(Get("perf.op.boost_applied"), false);
            });
        }

        #endregion

        #region Privacy Operations

        private async Task ClearActivityHistoryAsync()
        {
            var confirm = MessageBox.Show(this,
                Get("priv.op.clear_activity_confirm"),
                Get("priv.op.confirm_irreversible"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes) return;

            await ExecuteWithSafetyAsync(Get("privp.clear_activity_op"), async () =>
            {
                int filesDeleted = 0;
                var backups = new List<string>();

                // 1. Registry: disable timeline / activity history collection
                try
                {
                    var r1 = _registry.SetValueWithBackup(
                        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                        "Start_TrackDocs", 0, RegistryValueKind.DWord);
                    if (r1.Success) backups.Add(r1.Data!);
                }
                catch { }

                try
                {
                    var r2 = _registry.SetValueWithBackup(
                        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System",
                        "EnableActivityFeed", 0, RegistryValueKind.DWord);
                    if (r2.Success) backups.Add(r2.Data!);
                }
                catch { }

                try
                {
                    var r3 = _registry.SetValueWithBackup(
                        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System",
                        "PublishUserActivities", 0, RegistryValueKind.DWord);
                    if (r3.Success) backups.Add(r3.Data!);
                }
                catch { }

                try
                {
                    var r4 = _registry.SetValueWithBackup(
                        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System",
                        "UploadUserActivities", 0, RegistryValueKind.DWord);
                    if (r4.Success) backups.Add(r4.Data!);
                }
                catch { }

                // 2. (1.0 deleted the entire ConnectedDevicesPlatform folder here. 1.5 only removes
                //    the activity database below, which is what this feature is about.)

                // 3. Delete Timeline / ActivitiesCache database
                string actCachePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ConnectedDevicesPlatform");
                if (Directory.Exists(actCachePath))
                {
                    foreach (var dir in Directory.GetDirectories(actCachePath))
                    {
                        foreach (var file in Directory.EnumerateFiles(dir, "ActivitiesCache*"))
                        {
                            try { File.Delete(file); filesDeleted++; } catch { }
                        }
                    }
                }

                // 4. Clear Recent Items
                string recentPath = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                if (Directory.Exists(recentPath))
                {
                    foreach (var file in Directory.EnumerateFiles(recentPath, "*.lnk"))
                    {
                        try { File.Delete(file); filesDeleted++; } catch { }
                    }
                    // AutomaticDestinations (Jump Lists)
                    string autoDestPath = Path.Combine(recentPath, "AutomaticDestinations");
                    if (Directory.Exists(autoDestPath))
                    {
                        foreach (var file in Directory.EnumerateFiles(autoDestPath))
                        {
                            try { File.Delete(file); filesDeleted++; } catch { }
                        }
                    }
                    // CustomDestinations
                    string customDestPath = Path.Combine(recentPath, "CustomDestinations");
                    if (Directory.Exists(customDestPath))
                    {
                        foreach (var file in Directory.EnumerateFiles(customDestPath))
                        {
                            try { File.Delete(file); filesDeleted++; } catch { }
                        }
                    }
                }

                // 5. Clear Clipboard history (user data)
                string clipboardPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Windows", "Clipboard");
                if (Directory.Exists(clipboardPath))
                {
                    foreach (var file in Directory.EnumerateFiles(clipboardPath, "*", SearchOption.AllDirectories))
                    {
                        try { File.Delete(file); filesDeleted++; } catch { }
                    }
                }

                // Push undo for registry changes only (file deletions are irreversible)
                _operationHasIrreversibleSteps = filesDeleted > 0;
                if (backups.Count > 0)
                {
                    _safety.PushUndoAction(new UndoAction
                    {
                        Description = Get("priv.op.revert_activity_registry"),
                        BackupFiles = backups
                    });
                    UpdateUndoButton();
                }

                SetStatus(Get("priv.op.activity_cleared", filesDeleted, backups.Count), false);
                await Task.CompletedTask;
            });
        }

        // --- DNS Changer ---
        private static readonly string[][] DnsServers =
        {
            Array.Empty<string>(),                           // 0 = Auto (DHCP)
            new[] { "1.1.1.1", "1.0.0.1" },                // 1 = Cloudflare
            new[] { "8.8.8.8", "8.8.4.4" },                // 2 = Google
            new[] { "9.9.9.9", "149.112.112.112" },        // 3 = Quad9
            new[] { "208.67.222.222", "208.67.220.220" },  // 4 = OpenDNS
        };

        /// <summary>Provider names (brand names stay as they are; "automatic" is translated).</summary>
        private static string[] DnsNames => new[] { Get("privp.dns_auto"), "Cloudflare", "Google", "Quad9", "OpenDNS" };

        private static string? GetActiveInterfaceName()
        {
            string? name = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                    && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                ?.Name;

            // The name is placed inside quotes on the netsh command line: refuse anything that could break out.
            if (name == null || name.IndexOfAny(new[] { '"', '\r', '\n', '\0' }) >= 0)
                return null;
            return name;
        }

        // --- Hosts-based Ad Blocker ---
        private static readonly string HostsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "drivers", "etc", "hosts");

        private const string HostsMarkerStart = "# >>> K-win Ad Blocker Start >>>";
        private const string HostsMarkerEnd = "# <<< K-win Ad Blocker End <<<";

        private static string RemoveKwinHostsBlock(string content)
        {
            var lines = content.Split('\n').ToList();
            var result = new List<string>();
            bool inBlock = false;
            foreach (var line in lines)
            {
                if (line.TrimStart().StartsWith(HostsMarkerStart)) { inBlock = true; continue; }
                if (line.TrimStart().StartsWith(HostsMarkerEnd)) { inBlock = false; continue; }
                if (!inBlock) result.Add(line);
            }
            return string.Join('\n', result);
        }

        #endregion

        #region Cleanup Operations

        // The scan runs in the background; the label is updated after "await", which resumes on
        // the UI thread. (1.0 used BeginInvoke, which fails if the window isn't created yet.)
        private async Task ResetStoreCacheAsync()
        {
            var changes = new List<string>
            {
                Get("clean.op.store_run_wsreset"),
                Get("clean.op.store_window_appear"),
                Get("clean.op.store_timeout")
            };

            if (!PreviewDialog.ShowPreview(this, Get("clean.op.reset_store_title"), changes))
                return;

            SetBusy(true, Get("clean.op.resetting_store"));
            try
            {
                var result = await _cleanup.ResetStoreCacheAsync();
                SetStatus(result.Success
                    ? Get("clean.op.store_reset_ok")
                    : Get("clean.op.store_reset_fail", result.ErrorMessage ?? ""), !result.Success);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task RunSfcScanAsync()
        {
            var changes = new List<string>
            {
                Get("clean.op.sfc_run"),
                Get("clean.op.sfc_scans"),
                Get("clean.op.sfc_time"),
                Get("clean.op.sfc_no_cancel")
            };

            if (!PreviewDialog.ShowPreview(this, Get("clean.op.sfc_title"), changes))
                return;
            if (!EnsureAdmin(Get("clean.op.sfc_title"))) return;

            SetBusy(true, Get("clean.op.running_sfc"));
            _progressBar.Style = ProgressBarStyle.Marquee;
            try
            {
                var progress = new Progress<string>(msg => SetStatus(msg, false));
                var result = await _cleanup.RunSystemFileCheckAsync(progress);

                if (result.Success)
                {
                    SetStatus(Get("clean.op.sfc_complete"), false);
                    MessageBox.Show(this, string.IsNullOrWhiteSpace(result.Data) ? Get("clean.op.sfc_complete") : result.Data,
                        Get("clean.op.sfc_results"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    SetStatus(Get("failed", result.ErrorMessage ?? ""), true);
                }
                RecordOperation(Get("clean.op.sfc_title"), result.Success ? OperationStatus.Completed : OperationStatus.Failed,
                    Reversibility.None, null, result.ErrorMessage);
            }
            finally
            {
                _progressBar.Style = ProgressBarStyle.Continuous;
                SetBusy(false);
            }
        }

        private async Task RunDismCleanupAsync()
        {
            var changes = new List<string>
            {
                Get("clean.op.dism_run"),
                Get("clean.op.dism_removes"),
                Get("clean.op.dism_time")
            };

            if (!PreviewDialog.ShowPreview(this, Get("clean.op.dism_title"), changes))
                return;
            if (!EnsureAdmin(Get("clean.op.dism_title"))) return;

            SetBusy(true, Get("clean.op.running_dism"));
            _progressBar.Style = ProgressBarStyle.Marquee;
            try
            {
                var progress = new Progress<string>(msg => SetStatus(msg, false));
                var result = await _cleanup.RunDismCleanupAsync(progress);

                if (result.Success)
                {
                    SetStatus(Get("clean.op.dism_complete"), false);
                }
                else
                {
                    SetStatus(Get("failed", result.ErrorMessage ?? ""), true);
                }
                RecordOperation(Get("clean.op.dism_title"), result.Success ? OperationStatus.Completed : OperationStatus.Failed,
                    Reversibility.None, null, result.ErrorMessage);
            }
            finally
            {
                _progressBar.Style = ProgressBarStyle.Continuous;
                SetBusy(false);
            }
        }

        #endregion


        #region Apps Operations

        private async Task LoadAppsAsync(ComboBox filterCombo, ListView listView,
            Label countLabel, TextBox searchBox, List<ListViewItem> allItems)
        {
            SetBusy(true, Get("apps.loading_apps"));
            listView.Items.Clear();
            allItems.Clear();

            try
            {
                switch (filterCombo.SelectedIndex)
                {
                    case 0: // All Store Apps
                    {
                        var result = await _appManager.ListStoreAppsAsync();
                        if (result.Success)
                        {
                            foreach (var app in result.Data!)
                            {
                                if (AppManager.IsProtectedApp(app.PackageFullName)) continue;

                                var item = new ListViewItem(app.Name);
                                item.SubItems.Add(app.Publisher);
                                item.SubItems.Add(app.Version);
                                item.SubItems.Add("Store");
                                item.SubItems.Add(app.EstimatedSize);
                                item.Tag = app;
                                if (app.IsBloatware)
                                    item.ForeColor = Color.DarkOrange;
                                allItems.Add(item);
                            }
                        }
                        else
                        {
                            SetStatus(Get("failed", result.ErrorMessage ?? ""), true);
                        }
                        break;
                    }
                    case 1: // Bloatware only
                    {
                        var result = await _appManager.GetBloatwareStatusAsync();
                        if (result.Success)
                        {
                            foreach (var (pkgName, displayName, isInstalled, fullName) in result.Data!)
                            {
                                if (!isInstalled) continue;
                                var app = new AppManager.InstalledApp
                                {
                                    Name = displayName,
                                    PackageFullName = fullName,
                                    IsStoreApp = true,
                                    IsBloatware = true
                                };
                                var item = new ListViewItem(displayName);
                                item.SubItems.Add("");
                                item.SubItems.Add("");
                                item.SubItems.Add("Bloatware");
                                item.SubItems.Add("");
                                item.Tag = app;
                                item.ForeColor = Color.DarkOrange;
                                item.Checked = true; // Pre-select bloatware
                                allItems.Add(item);
                            }
                        }
                        break;
                    }
                    case 2: // Desktop Apps
                    {
                        var result = _appManager.ListDesktopApps();
                        if (result.Success)
                        {
                            foreach (var app in result.Data!)
                            {
                                var item = new ListViewItem(app.Name);
                                item.SubItems.Add(app.Publisher);
                                item.SubItems.Add(app.Version);
                                item.SubItems.Add("Desktop");
                                item.SubItems.Add(app.EstimatedSize);
                                item.Tag = app;
                                allItems.Add(item);
                            }
                        }
                        else
                        {
                            SetStatus(Get("failed", result.ErrorMessage ?? ""), true);
                        }
                        break;
                    }
                }

                // Apply search filter
                FilterAppList(listView, allItems, searchBox.Text);
                UpdateAppCount(listView, countLabel);
                SetStatus(Get("apps.loaded", allItems.Count), false);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void FilterAppList(ListView listView, List<ListViewItem> allItems, string searchText)
        {
            listView.BeginUpdate();
            listView.Items.Clear();

            foreach (var item in allItems)
            {
                if (string.IsNullOrWhiteSpace(searchText) ||
                    item.Text.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    (item.SubItems.Count > 1 &&
                     item.SubItems[1].Text.Contains(searchText, StringComparison.OrdinalIgnoreCase)))
                {
                    listView.Items.Add(item);
                }
            }

            listView.EndUpdate();
        }

        private static void UpdateAppCount(ListView listView, Label countLabel)
        {
            int total = listView.Items.Count;
            int selected = listView.CheckedItems.Count;
            countLabel.Text = selected > 0
                ? Get("apps.count_selected", total, selected)
                : Get("apps.count", total);
        }

        private async Task UninstallSelectedAppsAsync(ListView listView, ProgressBar progressBar,
            Label statusLabel, ComboBox filterCombo, Label countLabel, TextBox searchBox,
            List<ListViewItem> allItems)
        {
            var checkedItems = listView.CheckedItems;
            if (checkedItems.Count == 0)
            {
                MessageBox.Show(this, Get("apps.op.no_selected"),
                    Get("dialog.kwin"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Build list of apps to uninstall
            var appsToRemove = new List<AppManager.InstalledApp>();
            var appNames = new List<string>();
            foreach (ListViewItem item in checkedItems)
            {
                if (item.Tag is AppManager.InstalledApp app)
                {
                    appsToRemove.Add(app);
                    appNames.Add(app.Name);
                }
            }

            // Show confirmation using PreviewDialog
            var changes = new List<string> { Get("apps.op.will_uninstall", appsToRemove.Count) };
            changes.AddRange(appNames.Select(n => $"  • {n}"));

            if (!PreviewDialog.ShowPreview(this, Get("apps.op.uninstall_title"), changes,
                Get("apps.op.desktop_note")))
                return;

            // Execute
            progressBar.Visible = true;
            progressBar.Maximum = appsToRemove.Count;
            progressBar.Value = 0;

            int succeeded = 0;
            int failed = 0;
            var errors = new List<string>();

            foreach (var app in appsToRemove)
            {
                statusLabel.Text = Get("apps.op.uninstalling", app.Name);
                statusLabel.ForeColor = _isDarkMode ? DarkText : Color.Gray;

                Result<bool> result;
                if (app.IsStoreApp)
                    result = await _appManager.UninstallStoreAppAsync(app);
                else
                    result = await _appManager.UninstallDesktopAppAsync(app);

                if (result.Success)
                    succeeded++;
                else
                {
                    failed++;
                    errors.Add($"{app.Name}: {result.ErrorMessage}");
                }

                progressBar.Value = Math.Min(progressBar.Value + 1, progressBar.Maximum);
            }

            progressBar.Visible = false;

            RecordOperation(Get("apps.op.uninstall_title"),
                failed == 0 ? OperationStatus.Completed : OperationStatus.Failed, Reversibility.None,
                string.Join(", ", appNames), errors.Count > 0 ? string.Join(" | ", errors) : null);

            if (errors.Count > 0)
            {
                statusLabel.Text = Get("apps.op.done_results", succeeded, failed);
                statusLabel.ForeColor = Color.DarkOrange;

                MessageBox.Show(this,
                    Get("apps.op.errors_title", failed, string.Join("\n", errors)),
                    Get("apps.op.uninstall_results_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                statusLabel.Text = Get("apps.op.success_uninstall", succeeded);
                statusLabel.ForeColor = Color.Green;
                SetStatus(Get("apps.op.uninstalled_n", succeeded), false);
            }

            // Refresh list
            await LoadAppsAsync(filterCombo, listView, countLabel, searchBox, allItems);
        }

        private async Task OneClickBloatwareRemoveAsync(ProgressBar progressBar, Label statusLabel,
            ComboBox filterCombo, ListView listView, Label countLabel, TextBox searchBox,
            List<ListViewItem> allItems)
        {
            // First show what will be removed
            SetBusy(true, Get("apps.op.scanning_bloat"));
            var scanResult = await _appManager.GetBloatwareStatusAsync();
            SetBusy(false);

            if (!scanResult.Success)
            {
                SetStatus(Get("apps.op.scan_fail", scanResult.ErrorMessage ?? ""), true);
                return;
            }

            var installed = scanResult.Data!.Where(b => b.IsInstalled).ToList();
            if (installed.Count == 0)
            {
                MessageBox.Show(this, Get("apps.op.no_bloatware"),
                    Get("dialog.kwin"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var changes = new List<string>
            {
                Get("apps.op.bloat_found", installed.Count)
            };
            changes.AddRange(installed.Select(b => $"  • {b.DisplayName}"));
            changes.Add("");
            changes.Add(Get("apps.op.bloat_current_user"));
            changes.Add(Get("apps.op.bloat_all_users_optional"));

            if (!PreviewDialog.ShowPreview(this, Get("apps.op.bloat_title"), changes,
                Get("apps.op.bloat_warning")))
                return;

            // Advanced, explicitly confirmed option: also remove for all users (provisioned packages).
            bool includeAllUsers = MessageBox.Show(this,
                Get("apps.op.bloat_all_users_q"), Get("apps.op.remove_all_title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            if (includeAllUsers && !EnsureAdmin(Get("apps.op.remove_all_title")))
                return;

            // Execute
            progressBar.Visible = true;
            progressBar.Maximum = installed.Count;
            progressBar.Value = 0;
            statusLabel.ForeColor = _isDarkMode ? DarkText : Color.Gray;

            var progress = new Progress<(int current, int total, string name)>(p =>
            {
                statusLabel.Text = Get("apps.op.removing", p.current, p.total, p.name);
                progressBar.Value = Math.Min(p.current, progressBar.Maximum);
            });

            var result = await _appManager.RemoveBloatwareAsync(progress, includeAllUsers);
            progressBar.Visible = false;
            RecordOperation(Get("apps.op.bloat_title") + (includeAllUsers ? " (all users)" : ""),
                result.Success && result.Data.Failed == 0 ? OperationStatus.Completed : OperationStatus.Failed,
                Reversibility.None,
                result.Success ? string.Join(", ", result.Data.Details) : null,
                result.Success ? null : result.ErrorMessage);

            if (result.Success)
            {
                var (removed, failedCount, details) = result.Data;
                statusLabel.Text = Get("apps.op.done_results", removed, failedCount);
                statusLabel.ForeColor = failedCount == 0 ? Color.Green : Color.DarkOrange;

                string detailMsg = string.Join("\n", details);
                MessageBox.Show(this,
                    Get("apps.op.bloat_complete", removed, failedCount, detailMsg),
                    Get("apps.op.bloat_results_title"),
                    MessageBoxButtons.OK,
                    failedCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

                SetStatus(Get("apps.op.bloat_status", removed, failedCount), failedCount > 0);
            }
            else
            {
                statusLabel.Text = Get("failed", result.ErrorMessage ?? "");
                statusLabel.ForeColor = Color.Red;
            }

            // Refresh list
            await LoadAppsAsync(filterCombo, listView, countLabel, searchBox, allItems);
        }

        private async Task RemoveSelectedForAllUsersAsync(ListView listView,
            ProgressBar progressBar, Label statusLabel)
        {
            var checkedItems = listView.CheckedItems;
            if (checkedItems.Count == 0)
            {
                MessageBox.Show(this, Get("apps.op.no_selected_short"),
                    Get("dialog.kwin"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Only store apps can be removed for all users
            var storeApps = new List<AppManager.InstalledApp>();
            foreach (ListViewItem item in checkedItems)
            {
                if (item.Tag is AppManager.InstalledApp app && app.IsStoreApp)
                    storeApps.Add(app);
            }

            if (storeApps.Count == 0)
            {
                MessageBox.Show(this,
                    Get("apps.op.store_only"),
                    Get("dialog.kwin"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var changes = new List<string>
            {
                Get("apps.op.remove_all_desc", storeApps.Count),
            };
            changes.AddRange(storeApps.Select(a => $"  • {a.Name}"));
            changes.Add("");
            changes.Add(Get("apps.op.remove_all_provisioned"));

            if (!PreviewDialog.ShowPreview(this, Get("apps.op.remove_all_title"), changes,
                Get("apps.op.remove_all_warning")))
                return;

            // Second, explicit confirmation for this advanced option (default answer: No).
            if (MessageBox.Show(this, Get("apps.op.remove_all_confirm", storeApps.Count), Get("apps.op.remove_all_title"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            if (!EnsureAdmin(Get("apps.op.remove_all_title"))) return;

            progressBar.Visible = true;
            progressBar.Maximum = storeApps.Count;
            progressBar.Value = 0;

            int succeeded = 0;
            int failed = 0;

            foreach (var app in storeApps)
            {
                statusLabel.Text = Get("apps.op.removing_all", app.Name);
                statusLabel.ForeColor = _isDarkMode ? DarkText : Color.Gray;

                // Remove for current user first (it may already be gone for this user)
                var userResult = await _appManager.UninstallStoreAppAsync(app);
                if (!userResult.Success)
                    Logger.Instance.Warn("RemoveForAllUsers", app.Name, userResult.ErrorMessage ?? "");

                // Then remove provisioned
                string pkgName = app.PackageFullName;
                // Extract the base package name (before the underscore-version part)
                int underscoreIdx = pkgName.IndexOf('_');
                string baseName = underscoreIdx > 0 ? pkgName[..underscoreIdx] : pkgName;

                var result = await _appManager.RemoveProvisionedAppAsync(baseName);
                if (result.Success)
                    succeeded++;
                else
                    failed++;

                progressBar.Value = Math.Min(progressBar.Value + 1, progressBar.Maximum);
            }

            progressBar.Visible = false;
            RecordOperation(Get("apps.op.remove_all_title"),
                failed == 0 ? OperationStatus.Completed : OperationStatus.Failed, Reversibility.None,
                string.Join(", ", storeApps.Select(a => a.Name)), null);
            statusLabel.Text = Get("apps.op.done_all", succeeded, failed);
            statusLabel.ForeColor = failed == 0 ? Color.Green : Color.DarkOrange;
            SetStatus(Get("apps.op.removed_all", succeeded), failed > 0);
        }

        #endregion

        #region RAM Operations

        private void LoadTopProcesses(ListView listView)
        {
            listView.Items.Clear();
            var result = MemoryManager.GetTopMemoryProcesses(15);
            if (!result.Success) return;

            foreach (var proc in result.Data!)
            {
                var item = new ListViewItem(proc.Name);
                item.SubItems.Add(proc.WorkingSetMB.ToString("N0"));
                item.SubItems.Add(proc.PrivateBytesMB.ToString("N0"));
                item.SubItems.Add(proc.Pid.ToString());

                if (proc.WorkingSetMB > 500)
                    item.ForeColor = Color.Red;
                else if (proc.WorkingSetMB > 200)
                    item.ForeColor = Color.DarkOrange;

                listView.Items.Add(item);
            }
        }

        #endregion

        #region Safety & Undo

        /// <summary>
        /// Runs a system-changing operation with the full safety workflow:
        /// admin check, verified restore point (when enabled), registry change capture,
        /// automatic undo entry, failure detection and a saved history record.
        /// </summary>
        /// <summary>Warnings collected during the running operation (saved with its history record).</summary>
        private readonly List<string> _currentWarnings = new();

        private async Task ExecuteWithSafetyAsync(string operationName, Func<Task> action, bool requiresAdmin = true,
            Action<OperationRecord>? onRecorded = null)
        {
            if (DevMode.IsUiPreview)
            {
                ShowDevPreviewBlocked(operationName);
                return;
            }
            if (requiresAdmin && !EnsureAdmin(operationName))
                return;

            var record = new OperationRecord { Name = operationName };
            _statusErrorDuringOp = false;
            _lastErrorStatus = null;
            _operationHasIrreversibleSteps = false;
            _currentWarnings.Clear();

            SetBusy(true, Get("safety.applying", operationName));
            _safety.BeginOperation();
            try
            {
                if (_autoRestorePoint)
                {
                    if (ElevationHelper.IsAdministrator)
                    {
                        SetStatus(Get("safety.creating_rp"), false);
                        var rp = await Task.Run(() => _safety.CreateRestorePoint(operationName));
                        record.RestorePoint = rp.Status;

                        if (rp.Status == RestorePointStatus.Failed)
                        {
                            var answer = MessageBox.Show(this,
                                Get("safety.rp_failed_continue", rp.Message),
                                Get("safety.rp_failed_title"),
                                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                            if (answer != DialogResult.Yes)
                            {
                                record.Status = OperationStatus.Cancelled;
                                record.Details = rp.Message;
                                SetStatus(Get("safety.cancelled_no_rp"), false);
                                return;
                            }
                        }
                        else if (rp.Status == RestorePointStatus.SkippedRecentExists)
                        {
                            Logger.Instance.Info("ExecuteWithSafety", operationName, "restore_point_recent",
                                newValue: rp.LatestRestorePointTime?.ToString("g"));
                        }
                    }
                    else
                    {
                        // User-level change: no restore point possible without admin; registry backups still apply.
                        record.RestorePoint = RestorePointStatus.SkippedNotAdmin;
                    }
                }

                SetStatus(Get("safety.applying", operationName), false);
                _statusErrorDuringOp = false;
                await action();
                record.Status = _statusErrorDuringOp ? OperationStatus.Failed : OperationStatus.Completed;
                if (_statusErrorDuringOp) record.Error = _lastErrorStatus;
            }
            catch (Exception ex)
            {
                record.Status = OperationStatus.Failed;
                record.Error = $"{ex.GetType().Name}: {ex.Message}";
                Logger.Instance.Error("ExecuteWithSafety", operationName, ex);
                SetStatus(Get("failed", ex.Message), true);
                MessageBox.Show(this,
                    Get("safety.error_msg", operationName, ex.Message),
                    Get("safety.error_title"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                var capture = _safety.EndOperation(operationName);
                record.RegistryChanges = capture.Changes;
                record.UndoId = capture.UndoId;
                record.Reversibility = capture.UndoId == null
                    ? Reversibility.None
                    : (capture.HasSessionStep || _operationHasIrreversibleSteps) ? Reversibility.Partial : Reversibility.Full;

                if (capture.Failures.Count > 0 && record.Status != OperationStatus.Cancelled)
                {
                    record.Status = OperationStatus.Failed;
                    record.Error = string.Join(" | ", capture.Failures);
                }

                record.CompletedUtc = DateTime.UtcNow;
                record.Warnings.AddRange(_currentWarnings);
                _history.Add(record);
                onRecorded?.Invoke(record);
                UpdateUndoButton();
                SetBusy(false);

                if (capture.Failures.Count > 0 && record.Status != OperationStatus.Cancelled)
                {
                    SetStatus(Get("safety.partial_failure_status", capture.Failures.Count), true);
                    MessageBox.Show(this,
                        Get("safety.partial_failure", operationName, capture.Changes.Count,
                            string.Join("\n", capture.Failures.Take(8))),
                        Get("safety.error_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else if (record.Status == OperationStatus.Completed && record.RestorePoint == RestorePointStatus.SkippedRecentExists)
                {
                    SetStatus(Get("safety.done_rp_recent", operationName), false);
                }
            }
        }

        /// <summary>Same workflow for changes that only affect the current user (no admin rights needed).</summary>
        private Task ExecuteUserLevelAsync(string operationName, Func<Task> action) =>
            ExecuteWithSafetyAsync(operationName, action, requiresAdmin: false);

        /// <summary>
        /// Returns true when K-Win is elevated. Otherwise explains why administrator rights are needed
        /// and offers to restart K-Win elevated (the current window closes if the user accepts).
        /// </summary>
        private bool EnsureAdmin(string operationName)
        {
            if (ElevationHelper.IsAdministrator) return true;
            RequestElevation(operationName);
            return false;
        }

        private void RequestElevation(string operationName)
        {
            if (DevMode.IsUiPreview)
            {
                ShowDevPreviewBlocked(operationName);
                return;
            }
            var answer = MessageBox.Show(this,
                Get("elev.required_msg", operationName),
                Get("elev.required_title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer != DialogResult.Yes) return;

            if (ElevationHelper.TryRestartElevated(_currentPage))
                Close();
            else
                SetStatus(Get("elev.not_started"), false);
        }

        private void ShowDevPreviewBlocked(string operationName)
        {
            MessageBox.Show(this, Get("dev.blocked", operationName), Get("dev.preview_title"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>Adds an entry to the saved operation history.</summary>
        private void RecordOperation(string name, OperationStatus status, Reversibility reversibility,
            string? details, string? error)
        {
            _history.Add(new OperationRecord
            {
                Name = name,
                Status = status,
                Reversibility = reversibility,
                Details = details,
                Error = error,
                CompletedUtc = DateTime.UtcNow
            });
        }

        private async void UndoButton_Click(object? sender, EventArgs e)
        {
            if (_safety.UndoCount == 0)
            {
                MessageBox.Show(this, Get("safety.no_undo"), Get("dialog.kwin"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var next = _safety.PeekUndo();
            string desc = next?.Description ?? "";
            string? undoId = next?.Id;
            string message = Get("safety.undo_confirm", desc);
            if (next != null && next.IsPartial)
                message += "\n\n" + Get("safety.undo_partial_note");

            var confirm = MessageBox.Show(this, message, Get("safety.undo_title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            SetBusy(true, Get("safety.undoing"));
            try
            {
                var result = await _safety.UndoLastAction();
                if (result.Success) _history.MarkUndone(undoId);
                SetStatus(result.Success
                    ? Get("safety.undo_ok")
                    : Get("safety.undo_fail", result.ErrorMessage ?? ""), !result.Success);
                RecordOperation(Get("hist.undo_of", desc),
                    result.Success ? OperationStatus.Completed : OperationStatus.Failed,
                    Reversibility.None, null, result.ErrorMessage);
                if (!result.Success)
                {
                    MessageBox.Show(this, Get("safety.undo_fail", result.ErrorMessage ?? ""), Get("safety.undo_title"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                UpdateUndoButton();
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void UpdateUndoButton()
        {
            if (InvokeRequired)
            {
                BeginInvoke(UpdateUndoButton);
                return;
            }

            _undoButton.Enabled = _safety.UndoCount > 0;
            _undoButton.Text = _safety.UndoCount > 0
                ? Get("app.undo_n", _safety.UndoCount)
                : Get("app.undo");

            _undoToolTip.SetToolTip(_undoButton, _safety.UndoCount > 0 ? _safety.LastUndoDescription : null);
        }

        #endregion

        #region UI Helpers

        private void SetStatus(string message, bool isError)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => SetStatus(message, isError));
                return;
            }

            _statusLabel.Text = message;
            _statusLabel.ForeColor = isError ? ThemeManager.Current.Error : ThemeManager.Current.TextMuted;
            if (isError)
            {
                _statusErrorDuringOp = true;
                _lastErrorStatus = message;
            }
        }

        private void SetBusy(bool busy, string? message = null)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => SetBusy(busy, message));
                return;
            }

            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _progressBar.Visible = busy;
            _progressBar.Value = busy ? 50 : 0;

            if (message != null) SetStatus(message, false);
            if (!busy && message == null) SetStatus(Get("app.ready"), false);

            // Disable/enable the page content during operations (navigation stays available)
            _contentHost.Enabled = !busy;
        }

        private void LogViewerButton_Click(object? sender, EventArgs e)
        {
            string logPath = Logger.Instance.CurrentLogPath;
            if (File.Exists(logPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = logPath,
                    UseShellExecute = true
                });
            }
            else
            {
                System.Diagnostics.Process.Start("explorer.exe", Logger.Instance.LogDirectory);
            }
        }

        /// <summary>Applies the current theme to the shell, the new pages and the hosted 1.0 pages.</summary>
        private void ApplyTheme()
        {
            var p = ThemeManager.Current;
            SuspendLayout();
            BackColor = p.WindowBackground;
            ForeColor = p.Text;

            ThemeManager.ApplyTree(this);

            foreach (var host in _legacyHosts)
            {
                host.BackColor = p.WindowBackground;
                host.ForeColor = p.Text;
                ApplyLegacyTheme(host, p);
            }

            _statusLabel.ForeColor = p.TextMuted;
            foreach (var b in new[] { _undoButton, _logViewerButton })
            {
                b.BackColor = p.Input;
                b.ForeColor = p.Text;
                b.FlatAppearance.BorderColor = p.CardBorder;
                b.FlatAppearance.MouseOverBackColor = p.CardHover;
                b.FlatAppearance.MouseDownBackColor = p.SidebarHover;
            }
            foreach (var c in new[] { _themeCombo, _langCombo })
            {
                c.BackColor = p.Input;
                c.ForeColor = p.Text;
            }
            if (_adminStatusControl is LinkLabel link)
            {
                link.LinkColor = p.Accent;
                link.ActiveLinkColor = p.LogoYellow;
                link.VisitedLinkColor = p.Accent;
            }

            ThemeManager.ApplyTitleBar(this);
            SyncSelectors();
            ResumeLayout(true);
            Invalidate(true);
        }

        /// <summary>
        /// Theming for the 1.0 pages (standard controls at fixed positions): groups become cards,
        /// inputs use the input color. Labels with a status color (green/orange/red/gray) keep it.
        /// </summary>
        private static void ApplyLegacyTheme(Control parent, ThemePalette p)
        {
            foreach (Control c in parent.Controls)
            {
                switch (c)
                {
                    case GroupBox gb:
                        gb.BackColor = p.Card;
                        gb.ForeColor = p.Text;
                        ApplyLegacyTheme(gb, p);
                        continue;
                    case TableLayoutPanel tlp:
                        tlp.BackColor = p.WindowBackground;
                        ApplyLegacyTheme(tlp, p);
                        continue;
                    case Button btn:
                        if (btn.BackColor == AccentBlue ||
                            btn.BackColor == Color.FromArgb(200, 60, 60) ||
                            btn.BackColor == Color.FromArgb(0, 150, 80) ||
                            btn.BackColor == Color.FromArgb(0, 100, 180))
                            continue; // colored action buttons keep their color
                        btn.BackColor = p.Input;
                        btn.ForeColor = p.Text;
                        btn.FlatAppearance.BorderColor = p.CardBorder;
                        continue;
                    case TextBox or ComboBox or CheckedListBox or ListView or NumericUpDown:
                        c.BackColor = p.Input;
                        c.ForeColor = p.Text;
                        continue;
                    case LinkLabel link:
                        link.LinkColor = p.Accent;
                        continue;
                    case Label lbl:
                        if (lbl.ForeColor == Color.Green || lbl.ForeColor == Color.DarkOrange ||
                            lbl.ForeColor == Color.Red || lbl.ForeColor == Color.White)
                            continue;
                        lbl.ForeColor = lbl.ForeColor == Color.Gray ? p.TextMuted : p.Text;
                        continue;
                    case CheckBox or RadioButton:
                        c.ForeColor = p.Text;
                        continue;
                    case Panel panel:
                        panel.BackColor = panel.Parent is GroupBox ? p.Card : p.WindowBackground;
                        panel.ForeColor = p.Text;
                        ApplyLegacyTheme(panel, p);
                        continue;
                }
                ApplyLegacyTheme(c, p);
            }
        }

        private static bool IsFontInstalled(string fontName)
        {
            using var testFont = new Font(fontName, 10F, FontStyle.Regular, GraphicsUnit.Point);
            return testFont.Name.Equals(fontName, StringComparison.OrdinalIgnoreCase);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _undoToolTip.Dispose();
            _history.Changed -= OnHistoryChangedForShell;
            _monitor?.Dispose();
            Logger.Instance.Info("Application", "MainForm", "closing");
            Logger.Instance.Dispose();
            base.OnFormClosing(e);
        }

        #endregion
    }
}
