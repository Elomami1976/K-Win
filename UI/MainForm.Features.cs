using System.Diagnostics;
using KWin.Controls;
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
    /// Operations behind the 1.5 feature pages (Performance, Privacy, Startup, Cleanup, Tools).
    /// The pages hold no system logic; everything that changes the system goes through
    /// ExecuteWithSafetyAsync (admin check, restore point, change capture, undo, history).
    /// </summary>
    public partial class MainForm
    {
        private TweakEngine _tweaks = null!;
        private StartupService _startup = null!;

        private void InitFeatureServices()
        {
            _tweaks = new TweakEngine(_registry);
            _startup = new StartupService(_registry);
        }

        #region Data-driven settings

        private SettingActions CreateSettingActions() => new()
        {
            Read = d => _tweaks.ReadState(d),
            Apply = ApplySettingAsync,
            UndoText = d => d.Id is "telemetry" or "sysmain" or "fast_startup" ? Get("card.undo_mixed") : null
        };

        /// <summary>Extra, non-registry step of a setting, shown in the preview.</summary>
        private static string? HookDescription(SettingDefinition d, bool on) => d.Id switch
        {
            "telemetry" => Get(on ? "hook.diagtrack_stop" : "hook.diagtrack_start"),
            "sysmain" => Get(on ? "hook.sysmain_start" : "hook.sysmain_stop"),
            "fast_startup" => Get(on ? "hook.hibernate_on" : "hook.hibernate_off"),
            "classic_context_menu" => Get("hook.explorer_restart"),
            _ => null
        };

        /// <summary>
        /// Preview -> safety workflow -> verified registry writes (undoable) -> extra step (service,
        /// powercfg, Explorer restart) with a session undo step.
        /// </summary>
        private async Task ApplySettingAsync(SettingDefinition d, bool on)
        {
            string title = Get(d.TextKey("title"));
            string target = Get(d.TextKey(on ? "on" : "off"));

            if (d.IsPolicy && OrgManagement.IsManaged)
            {
                MessageBox.Show(this, Get("card.managed_desc"), title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var lines = new List<string> { Get("card.preview_change", target) };
            lines.AddRange(TweakEngine.DescribeChanges(d, on));
            if (HookDescription(d, on) is string hook) lines.Add(hook);
            lines.Add("");
            lines.Add($"{Get("card.side_effects")}: {Get(d.TextKey("side"))}");
            if (d.RestartRequired) lines.Add(Get("card.restart_needed"));
            else if (d.SignOutRequired) lines.Add(Get("card.signout_needed"));
            lines.Add(Get("card.preview_undo"));

            if (!PreviewDialog.ShowPreview(this, $"{title}: {target}", lines))
                return;

            await ExecuteWithSafetyAsync($"{title}: {target}", async () =>
            {
                var r = _tweaks.Apply(d, on);
                if (!r.Success)
                {
                    SetStatus(Get("card.apply_failed", title, r.ErrorMessage ?? ""), true);
                    return;
                }

                var (hookError, undoStep) = await RunSettingHookAsync(d, on);
                if (hookError != null) SetStatus(hookError, true);

                _safety.PushUndoAction(new UndoAction { Description = $"{title}: {target}", UndoDelegate = undoStep });
                if (hookError == null)
                    SetStatus(Get("card.applied", title, target), false);
            }, requiresAdmin: d.RequiresAdmin);
        }

        private async Task<(string? Error, Func<Task<Result<bool>>>? Undo)> RunSettingHookAsync(SettingDefinition d, bool on)
        {
            switch (d.Id)
            {
                case "telemetry":
                case "sysmain":
                {
                    string service = d.Id == "telemetry" ? "DiagTrack" : "SysMain";
                    bool wasRunning = SystemTools.IsServiceRunning(service);
                    // Telemetry "On" = more private = service stopped. SysMain "On" = service running.
                    bool shouldRun = d.Id == "telemetry" ? !on : on;
                    var r = shouldRun ? await SystemTools.StartServiceAsync(service) : await SystemTools.StopServiceAsync(service);
                    Func<Task<Result<bool>>> undo = wasRunning
                        ? () => SystemTools.StartServiceAsync(service)
                        : () => SystemTools.StopServiceAsync(service);
                    return (r.Success ? null : Get("hook.service_failed", service, r.ErrorMessage ?? ""), undo);
                }
                case "fast_startup":
                {
                    var r = await _systemTools.ExecuteToolAsync("powercfg.exe", on ? "/hibernate on" : "/hibernate off");
                    async Task<Result<bool>> Undo()
                    {
                        var u = await _systemTools.ExecuteToolAsync("powercfg.exe", on ? "/hibernate off" : "/hibernate on");
                        return u.Success ? Result<bool>.Ok(true) : Result<bool>.Fail(u.ErrorMessage ?? "powercfg failed");
                    }
                    return (r.Success ? null : Get("failed", r.ErrorMessage ?? "powercfg"), Undo);
                }
                case "classic_context_menu":
                    await RestartExplorerAsync();
                    return (null, async () => { await RestartExplorerAsync(); return Result<bool>.Ok(true); });
                default:
                    return (null, null);
            }
        }

        private static async Task RestartExplorerAsync()
        {
            try
            {
                DevMode.ThrowIfReadOnly("restart Explorer");
                foreach (var proc in Process.GetProcessesByName("explorer"))
                    using (proc) proc.Kill();
                await Task.Delay(800);
                // Windows restarts the shell by itself; start it only if it did not come back.
                if (Process.GetProcessesByName("explorer").Length == 0)
                    Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Instance.Warn("RestartExplorer", "explorer.exe", ex.Message);
            }
        }

        #endregion

        #region Performance

        private PerformanceActions CreatePerformanceActions() => new()
        {
            Settings = CreateSettingActions(),
            LoadPowerPlans = async () =>
            {
                var r = await _systemTools.ListPowerPlansAsync();
                return r.Success && r.Data != null ? r.Data : new List<(string, string, bool)>();
            },
            ApplyPowerPlan = ApplyPowerPlanAsync,
            CreateUltimatePlan = CreateUltimatePlanAsync,
            GetMemoryCompression = MemoryManager.GetMemoryCompressionAsync,
            SetMemoryCompression = SetMemoryCompressionAsync,
            RunBoost = () => OneClickPerformanceBoostAsync(disableTransparency: true)
        };

        private async Task CreateUltimatePlanAsync()
        {
            var changes = new List<string> { Get("perf.op.create_ultimate"), Get("perf.op.create_ultimate_appear") };
            if (!PreviewDialog.ShowPreview(this, Get("perf.op.create_ultimate_title"), changes))
                return;
            await ExecuteUserLevelAsync(Get("perf.op.create_ultimate_title"), async () =>
            {
                var result = await _systemTools.CreateUltimatePerformancePlanAsync();
                SetStatus(result.Success ? result.Data ?? Get("perf.op.ultimate_created") : Get("failed", result.ErrorMessage ?? ""), !result.Success);
            });
        }

        private async Task SetMemoryCompressionAsync(bool enable)
        {
            string title = Get("perfp.comp_title");
            string target = Get(enable ? "perfp.comp_on" : "perfp.comp_off");
            var lines = new List<string>
            {
                Get("card.preview_change", target),
                enable ? "PowerShell: Enable-MMAgent -MemoryCompression" : "PowerShell: Disable-MMAgent -MemoryCompression",
                "",
                $"{Get("card.side_effects")}: {Get("perfp.comp_side")}",
                Get("card.restart_needed"),
                Get("card.undo_session")
            };
            if (!PreviewDialog.ShowPreview(this, $"{title}: {target}", lines))
                return;

            await ExecuteWithSafetyAsync($"{title}: {target}", async () =>
            {
                var r = await MemoryManager.SetMemoryCompressionAsync(enable);
                if (!r.Success)
                {
                    SetStatus(Get("card.apply_failed", title, r.ErrorMessage ?? ""), true);
                    return;
                }
                _safety.PushUndoAction(new UndoAction
                {
                    Description = $"{title}: {target}",
                    UndoDelegate = () => MemoryManager.SetMemoryCompressionAsync(!enable)
                });
                SetStatus(Get("card.applied_restart", title, target), false);
            });
        }

        #endregion

        #region Privacy

        private PrivacyActions CreatePrivacyActions() => new()
        {
            Settings = CreateSettingActions(),
            GetAdvertisingId = () => _registry.GetValue(@"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Id").Data as string,
            ResetAdvertisingId = ResetAdvertisingIdAsync,
            ClearActivityData = ClearActivityHistoryAsync,
            DnsNames = DnsNames,
            GetDnsStatus = GetDnsStatusText,
            SetDns = SetDnsAsync,
            GetHostsStatus = GetHostsStatus,
            RemoveHostsBlock = RemoveHostsBlockAsync,
            ShowHostsDownloadDisabled = () => MessageBox.Show(this, Get("priv.hosts_download_disabled"), Get("dialog.kwin"),
                MessageBoxButtons.OK, MessageBoxIcon.Information),
            OpenWindowsSecurity = () => _systemTools.OpenWindowsSettings("ms-settings:windowsdefender")
        };

        private async Task ResetAdvertisingIdAsync()
        {
            var changes = new List<string>
            {
                @"Registry: HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo\Id = (new random ID)",
                Get("privp.reset_ad_desc"),
                Get("card.preview_undo")
            };
            if (!PreviewDialog.ShowPreview(this, Get("priv.op.reset_ad_title"), changes, Get("priv.op.ad_replaces_warning")))
                return;

            await ExecuteUserLevelAsync(Get("priv.op.reset_ad_title"), async () =>
            {
                var r = _registry.SetValueWithBackup(@"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
                    "Id", Guid.NewGuid().ToString(), Microsoft.Win32.RegistryValueKind.String);
                SetStatus(r.Success ? Get("priv.op.ad_reset_done") : Get("failed", r.ErrorMessage ?? ""), !r.Success);
                await Task.CompletedTask;
            });
        }

        private string GetDnsStatusText()
        {
            try
            {
                var ni = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                        && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback);
                if (ni == null) return Get("priv.dns_no_adapter");
                var dns = ni.GetIPProperties().DnsAddresses;
                if (dns.Count == 0) return Get("priv.dns_status_auto");
                string primary = dns[0].ToString();
                for (int i = 1; i < DnsServers.Length; i++)
                    if (DnsServers[i][0] == primary)
                        return Get("priv.dns_status_set", DnsNames[i], Loc.Ltr(primary));
                return Get("priv.dns_status_custom", Loc.Ltr(primary));
            }
            catch
            {
                return Get("common.unable_determine");
            }
        }

        private async Task SetDnsAsync(int index)
        {
            if (index < 0 || index >= DnsServers.Length) return;
            if (DevMode.IsUiPreview) { ShowDevPreviewBlocked(Get("priv.op.dns_title")); return; }
            if (!EnsureAdmin(Get("priv.op.dns_title"))) return;

            string name = DnsNames[index];
            var changes = index == 0
                ? new List<string> { Get("priv.dns_set_auto") }
                : new List<string> { Get("priv.op.dns_set_desc", name, DnsServers[index][0], DnsServers[index][1]) };
            changes.Add(Get("privp.dns_undo"));
            if (!PreviewDialog.ShowPreview(this, Get("priv.op.dns_title"), changes))
                return;

            SetBusy(true, Get("priv.dns_applying", name));
            try
            {
                var iface = GetActiveInterfaceName();
                if (iface == null) { SetStatus(Get("priv.dns_no_adapter"), true); return; }

                // Servers come from the fixed list; the adapter name is validated.
                Result<string> r1, r2;
                if (index == 0)
                {
                    r1 = r2 = await _systemTools.ExecuteToolAsync("netsh.exe", $"interface ip set dns name=\"{iface}\" dhcp");
                }
                else
                {
                    r1 = await _systemTools.ExecuteToolAsync("netsh.exe", $"interface ip set dns name=\"{iface}\" static {DnsServers[index][0]}");
                    r2 = r1.Success
                        ? await _systemTools.ExecuteToolAsync("netsh.exe", $"interface ip add dns name=\"{iface}\" {DnsServers[index][1]} index=2")
                        : r1;
                }
                bool ok = r1.Success && r2.Success;
                string? error = ok ? null : (r1.Success ? r2 : r1).ErrorMessage;
                SetStatus(ok ? Get("priv.dns_set_ok", name) : Get("failed", error ?? ""), !ok);
                RecordOperation($"{Get("priv.op.dns_title")} ({name})", ok ? OperationStatus.Completed : OperationStatus.Failed,
                    Reversibility.None, Get("hist.dns_revert_hint"), error);
            }
            catch (Exception ex)
            {
                SetStatus(Get("failed", ex.Message), true);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private (bool Active, string Status) GetHostsStatus()
        {
            try
            {
                if (File.Exists(HostsPath))
                {
                    var content = File.ReadAllText(HostsPath);
                    if (content.Contains(HostsMarkerStart))
                    {
                        int count = 0;
                        bool inBlock = false;
                        foreach (var line in content.Split('\n'))
                        {
                            if (line.TrimStart().StartsWith(HostsMarkerStart)) { inBlock = true; continue; }
                            if (line.TrimStart().StartsWith(HostsMarkerEnd)) { inBlock = false; continue; }
                            if (inBlock && line.StartsWith("0.0.0.0")) count++;
                        }
                        return (true, Get("priv.hosts_active", count));
                    }
                }
                return (false, Get("priv.hosts_inactive"));
            }
            catch
            {
                return (false, Get("common.unable_determine"));
            }
        }

        private async Task RemoveHostsBlockAsync()
        {
            if (!GetHostsStatus().Active)
            {
                SetStatus(Get("priv.hosts_inactive"), false);
                return;
            }
            if (DevMode.IsUiPreview) { ShowDevPreviewBlocked(Get("priv.hosts_disable")); return; }
            if (!EnsureAdmin(Get("priv.hosts_disable"))) return;

            SetBusy(true, Get("priv.hosts_removing"));
            try
            {
                var content = File.ReadAllText(HostsPath);
                string backup = Path.Combine(_safety.BackupDirectory, $"hosts_{DateTime.Now:yyyyMMdd_HHmmss}.bak");
                File.Copy(HostsPath, backup, overwrite: false);

                File.WriteAllText(HostsPath, RemoveKwinHostsBlock(content).TrimEnd() + Environment.NewLine);
                await _systemTools.ExecuteToolAsync("ipconfig.exe", "/flushdns");

                SetStatus(Get("priv.hosts_disabled_ok"), false);
                RecordOperation(Get("priv.hosts_disable"), OperationStatus.Completed, Reversibility.None, Get("hist.backup_at", backup), null);
            }
            catch (Exception ex)
            {
                SetStatus(Get("failed", ex.Message), true);
                RecordOperation(Get("priv.hosts_disable"), OperationStatus.Failed, Reversibility.None, null, ex.Message);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        #endregion

        #region Startup

        private StartupActions CreateStartupActions() => new()
        {
            Load = () => Task.Run(() =>
            {
                var list = _startup.Discover();
                foreach (var e in list) StartupService.FillDetails(e);
                return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            }),
            SetEnabled = SetStartupEnabledAsync
        };

        private async Task SetStartupEnabledAsync(IReadOnlyList<StartupEntry> entries, bool enable)
        {
            string title = Get(enable ? "startup.op_enable" : "startup.op_disable", entries.Count);
            var lines = new List<string> { Get(enable ? "startup.op_enable_desc" : "startup.op_disable_desc") };
            lines.AddRange(entries.Select(e => $"  • {e.Name}  ({StartupPage.SourceText(e.Source)})"));
            lines.Add("");
            lines.Add(Get("startup.op_note"));
            lines.Add(Get("card.preview_undo"));
            if (!PreviewDialog.ShowPreview(this, title, lines))
                return;

            bool needsAdmin = entries.Any(e => e.IsMachineWide);
            await ExecuteWithSafetyAsync(title, async () =>
            {
                int ok = 0;
                foreach (var e in entries)
                {
                    var r = _startup.SetEnabled(e, enable);
                    if (r.Success) ok++;
                    else SetStatus(Get("startup.op_failed", e.Name, r.ErrorMessage ?? ""), true);
                }
                if (ok == entries.Count) SetStatus(Get("startup.op_done", ok), false);
                await Task.CompletedTask;
            }, requiresAdmin: needsAdmin);
        }

        #endregion

        #region Cleanup

        private CleanupActions CreateCleanupActions() => new()
        {
            Execute = ExecuteCleanupAsync,
            ResetStoreCache = ResetStoreCacheAsync
        };

        private async Task<List<CategoryResult>?> ExecuteCleanupAsync(IReadOnlyList<CleanupCategory> categories,
            IProgress<string> progress, CancellationToken ct)
        {
            var selected = categories.Where(c => c.Selected).ToList();
            if (selected.Count == 0) return null;
            if (DevMode.IsUiPreview) { ShowDevPreviewBlocked(Get("cleanup.op_name")); return null; }

            var lines = new List<string> { Get("cleanup.preview_intro") };
            lines.AddRange(selected.Select(c =>
                $"  • {c.Name}: {(c.MeasuredBytes is long b ? CleanupManager.FormatSize(b) : Get("dash.unavailable"))}"));
            lines.Add("");
            lines.Add(Get("clean.op.temp_age_note"));
            lines.Add(Get("clean.op.browser_keeps_data"));
            if (selected.Any(c => c.Kind == CleanupKind.RecycleBin)) lines.Add(Get("cleanup.preview_recycle"));
            var running = CleanupManager.GetRunningBrowserWarnings();
            string? warning = running.Count > 0 ? string.Join("\n", running) : null;
            lines.Add(Get("clean.op.irreversible_note"));

            if (!PreviewDialog.ShowPreview(this, Get("cleanup.op_name"), lines, warning))
                return null;

            SetStatus(Get("clean.op.cleaning_temp"), false);
            var results = await new CleanupPlanner().ExecuteAsync(categories, progress, ct);

            long freed = results.Sum(r => r.Report.BytesFreed);
            int files = results.Sum(r => r.Report.FilesDeleted);
            int skipped = results.Sum(r => r.Report.FilesSkipped);
            bool cancelled = results.Any(r => r.Outcome is CategoryOutcome.Cancelled or CategoryOutcome.NotRun);
            var failed = results.Where(r => r.Outcome == CategoryOutcome.Failed).ToList();
            string summary = Get("clean.op.cleaned_files_skipped", files, CleanupManager.FormatSize(freed), skipped);

            RecordOperation(Get("cleanup.op_name"),
                failed.Count > 0 ? OperationStatus.Failed : cancelled ? OperationStatus.Cancelled : OperationStatus.Completed,
                Reversibility.None,
                summary + " · " + string.Join(", ", results.Select(r => $"{r.Category.Name}: {r.Outcome}")),
                failed.Count > 0 ? string.Join(" | ", failed.Select(f => $"{f.Category.Name}: {f.Error}")) : null);
            SetStatus(summary, failed.Count > 0);
            return results;
        }

        #endregion

        #region Tools: Apps and Memory sections

        /// <summary>Apps section (1.0 operations, new layout).</summary>
        private Control BuildAppsSection()
        {
            var card = new RoundedPanel { Kind = SurfaceKind.Card, Padding = new Padding(14) };
            var grid = Ui.Grid(1, 5);
            Ui.Rows(grid, (SizeType.Absolute, 44), (SizeType.Percent, 100), (SizeType.Absolute, 26), (SizeType.Absolute, 46), (SizeType.Absolute, 40));

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            var filter = new ThemedComboBox { Width = 220, Font = UiFonts.Regular(9.5f), Margin = new Padding(0, 6, 10, 0) };
            filter.Items.AddRange(new object[] { Get("apps.all_store"), Get("apps.bloatware_only"), Get("apps.desktop_apps") });
            var search = new TextBox { Width = 220, Font = UiFonts.Regular(10f), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 6, 10, 0), PlaceholderText = Get("startup.search") };
            var selectAll = new CheckBox { Text = Get("apps.select_all"), AutoSize = true, Font = UiFonts.Regular(9.5f), Margin = new Padding(0, 9, 10, 0) };
            toolbar.Controls.Add(filter);
            toolbar.Controls.Add(search);
            toolbar.Controls.Add(selectAll);
            grid.Controls.Add(toolbar, 0, 0);

            var list = new ThemedListView { Dock = DockStyle.Fill, CheckBoxes = true, MultiSelect = true };
            list.Columns.Add(Get("apps.col_name"), 260);
            list.Columns.Add(Get("apps.col_publisher"), 180);
            list.Columns.Add(Get("apps.col_version"), 110);
            list.Columns.Add(Get("apps.col_type"), 80);
            list.Columns.Add(Get("apps.col_size"), 90);
            grid.Controls.Add(list, 0, 1);

            var count = new ThemedLabel { Dock = DockStyle.Fill, Role = TextRole.Muted, Font = UiFonts.Regular(9f) };
            grid.Controls.Add(count, 0, 2);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            var progress = new ProgressBar { Width = 160, Height = 8, Visible = false, Margin = new Padding(0, 18, 10, 0) };
            var status = new ThemedLabel { AutoSize = true, Role = TextRole.Muted, Font = UiFonts.Regular(9f), Margin = new Padding(0, 14, 0, 0), AutoEllipsis = false };
            var all = new List<ListViewItem>();

            AccentButton Btn(string text, bool primary, Func<Task> onClick)
            {
                var b = new AccentButton { Text = text, Secondary = !primary, FontSize = 9.5f, Height = 34, Margin = new Padding(0, 4, 8, 0) };
                using (var g = CreateGraphics())
                    b.Width = TextRenderer.MeasureText(g, text, UiFonts.Semibold(9.5f)).Width + Draw.S(this, 36);
                b.Click += async (s, e) => await onClick();
                return b;
            }

            actions.Controls.Add(Btn(Get("apps.uninstall_selected"), true,
                () => UninstallSelectedAppsAsync(list, progress, status, filter, count, search, all)));
            actions.Controls.Add(Btn(Get("apps.remove_bloatware"), false,
                () => OneClickBloatwareRemoveAsync(progress, status, filter, list, count, search, all)));
            actions.Controls.Add(Btn(Get("apps.remove_all_users"), false,
                async () =>
                {
                    await RemoveSelectedForAllUsersAsync(list, progress, status);
                    await LoadAppsAsync(filter, list, count, search, all);
                }));
            actions.Controls.Add(progress);
            actions.Controls.Add(status);
            grid.Controls.Add(actions, 0, 3);

            grid.Controls.Add(Ui.Text(Get("toolsp.apps_note"), UiFonts.Regular(9f), TextRole.Muted, wrap: true), 0, 4);

            filter.SelectedIndexChanged += async (s, e) => await LoadAppsAsync(filter, list, count, search, all);
            search.TextChanged += (s, e) => { FilterAppList(list, all, search.Text); UpdateAppCount(list, count); };
            selectAll.CheckedChanged += (s, e) => { foreach (ListViewItem i in list.Items) i.Checked = selectAll.Checked; };
            card.VisibleChanged += (s, e) =>
            {
                if (card.Visible && filter.SelectedIndex < 0) filter.SelectedIndex = 0; // first load
            };
            card.Controls.Add(grid);
            return card;
        }

        /// <summary>Memory section: real memory figures, top processes, SysMain setting.</summary>
        private Control BuildMemorySection(SettingActions settings)
        {
            var page = new Panel();
            var root = Ui.Grid(2, 1);
            Ui.Columns(root, (SizeType.Percent, 58), (SizeType.Percent, 42));

            var left = new RoundedPanel { Kind = SurfaceKind.Card, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0), Padding = new Padding(14) };
            var lgrid = Ui.Grid(1, 4);
            Ui.Rows(lgrid, (SizeType.Absolute, 30), (SizeType.Absolute, 26), (SizeType.Absolute, 26), (SizeType.Percent, 100));
            lgrid.Controls.Add(Ui.Text(Get("toolsp.mem_title"), UiFonts.Semibold(11.5f)), 0, 0);
            var usage = Ui.Text("", UiFonts.Regular(10f));
            var pagefile = Ui.Text("", UiFonts.Regular(9f), TextRole.Muted);
            lgrid.Controls.Add(usage, 0, 1);
            lgrid.Controls.Add(pagefile, 0, 2);

            var procs = new ThemedListView { Dock = DockStyle.Fill };
            procs.Columns.Add(Get("ram.col_process"), 220);
            procs.Columns.Add(Get("ram.col_ram_mb"), 110);
            procs.Columns.Add(Get("ram.col_private_mb"), 110);
            procs.Columns.Add(Get("ram.col_pid"), 80);
            lgrid.Controls.Add(procs, 0, 3);
            left.Controls.Add(lgrid);
            root.Controls.Add(left, 0, 0);

            SettingCard? sysMain = null;
            sysMain = CardFactory.Setting(SettingCatalog.SysMain, settings, () => sysMain?.RefreshState());
            sysMain.Margin = new Padding(6, 0, 0, 0);
            root.Controls.Add(sysMain, 1, 0);

            void Refresh()
            {
                var mem = MemoryManager.GetMemoryInfo();
                usage.Text = mem.Success
                    ? Get("toolsp.mem_usage", mem.Data!.MemoryLoadPercent, Loc.Ltr(mem.Data.UsedFormatted), Loc.Ltr(mem.Data.TotalFormatted), Loc.Ltr(mem.Data.AvailableFormatted))
                    : Get("dash.unavailable");
                var pf = MemoryManager.GetPageFileSizeInfo();
                pagefile.Text = pf.Success ? Loc.Ltr(pf.Data!) : "";
                LoadTopProcesses(procs);
                sysMain!.RefreshState();
            }

            page.VisibleChanged += (s, e) => { if (page.Visible) Refresh(); };
            page.Controls.Add(root);
            return page;
        }

        #endregion
    }
}
