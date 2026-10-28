using System.Globalization;
using KWin.Controls;
using KWin.Core;
using KWin.Models;
using KWin.Services;
using KWin.Themes;
using KWin.Utils;
using static KWin.Utils.Loc;

namespace KWin.UI.Pages
{
    /// <summary>Actions the dashboard asks the main window to perform.</summary>
    public sealed class DashboardActions
    {
        public required Action<int> Navigate { get; init; }
        /// <summary>Opens the existing one-click performance boost (with its preview and confirmation).</summary>
        public required Action OptimizeNow { get; init; }
        public required Action CleanTempFiles { get; init; }
        public required Action ViewHistory { get; init; }
    }

    /// <summary>
    /// Dashboard: system information, live CPU/RAM/disk values, quick actions, recent actions and
    /// disk space. Every number shown is measured; values that can't be measured are shown as
    /// not available. (Charts and GPU usage arrive with the Phase 4 monitoring.)
    /// </summary>
    public class DashboardPage : UserControl, IThemedControl
    {
        private const int RecentRows = 4;
        private const int DiskRows = 3;

        private readonly DashboardActions _actions;
        private readonly OperationHistoryService _history;
        private readonly SystemMonitorService _monitor;
        private StaticSystemInfo? _info;
        private DateTime _lastDiskRefresh = DateTime.MinValue;

        // System card
        private readonly ThemedLabel _osTitle, _osVersion, _osDevice, _osCpu, _osRam, _osGpu;
        private readonly IconBadge _statusIcon;
        private readonly ThemedLabel _statusValue, _statusText;

        // Metric tiles
        private readonly MetricTile _cpuTile, _memTile, _diskTile, _gpuTile;

        // Recent actions
        private readonly (IconBadge Icon, ThemedLabel Name, ThemedLabel When)[] _recent = new (IconBadge, ThemedLabel, ThemedLabel)[RecentRows];
        private readonly ThemedLabel _recentEmpty;
        private readonly TableLayoutPanel _recentList;

        // Disk space
        private readonly (Control Row, ThemedLabel Name, UsageBar Bar, ThemedLabel Used, ThemedLabel Percent)[] _disks =
            new (Control, ThemedLabel, UsageBar, ThemedLabel, ThemedLabel)[DiskRows];

        public DashboardPage(DashboardActions actions, OperationHistoryService history, SystemMonitorService monitor)
        {
            _actions = actions;
            _history = history;
            _monitor = monitor;
            DoubleBuffered = true;
            AutoScroll = true;
            Padding = new Padding(0);

            var root = Ui.Grid(1, 4);
            Ui.Rows(root, (SizeType.Absolute, 168), (SizeType.Absolute, 156), (SizeType.Absolute, 140), (SizeType.Percent, 100));
            Ui.Columns(root, (SizeType.Percent, 100));

            // ---------------- System card ----------------
            var sysCard = Card();
            var sys = Ui.Grid(4, 1, new Padding(18, 10, 18, 10));
            Ui.Columns(sys, (SizeType.Absolute, 120), (SizeType.Percent, 55), (SizeType.Percent, 45), (SizeType.Absolute, 250));

            var pcArt = new IconBadge { Glyph = Glyphs.Pc, GlyphSize = 44f, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 12, 6) };
            sys.Controls.Add(pcArt, 0, 0);

            var info = Ui.Grid(1, 6);
            Ui.Rows(info, (SizeType.Absolute, 34), (SizeType.Absolute, 22), (SizeType.Absolute, 22),
                (SizeType.Absolute, 22), (SizeType.Absolute, 22), (SizeType.Absolute, 22));
            _osTitle = Ui.Text(Get("dash.loading"), UiFonts.Semibold(15f));
            _osVersion = Ui.Text("", UiFonts.Regular(9.5f), TextRole.Muted);
            _osDevice = Ui.Text("", UiFonts.Regular(9.5f), TextRole.Muted);
            _osCpu = Ui.Text("", UiFonts.Regular(9.5f), TextRole.Muted);
            _osRam = Ui.Text("", UiFonts.Regular(9.5f), TextRole.Muted);
            _osGpu = Ui.Text("", UiFonts.Regular(9.5f), TextRole.Muted);
            info.Controls.Add(_osTitle, 0, 0);
            info.Controls.Add(_osVersion, 0, 1);
            info.Controls.Add(_osDevice, 0, 2);
            info.Controls.Add(_osCpu, 0, 3);
            info.Controls.Add(_osRam, 0, 4);
            info.Controls.Add(_osGpu, 0, 5);
            sys.Controls.Add(info, 1, 0);

            var status = Ui.Grid(2, 1);
            Ui.Columns(status, (SizeType.Absolute, 76), (SizeType.Percent, 100));
            _statusIcon = new IconBadge { Glyph = Glyphs.Privacy, GlyphSize = 30f, Dock = DockStyle.Fill, Margin = new Padding(8, 24, 10, 24) };
            var statusText = Ui.Grid(1, 3);
            Ui.Rows(statusText, (SizeType.Absolute, 34), (SizeType.Absolute, 36), (SizeType.Percent, 100));
            statusText.Padding = new Padding(0, 14, 0, 0);
            statusText.Controls.Add(Ui.Text(Get("dash.system_status"), UiFonts.Semibold(11.5f)), 0, 0);
            _statusValue = Ui.Text("…", UiFonts.Semibold(16f), TextRole.Success);
            _statusText = Ui.Text("", UiFonts.Regular(9f), TextRole.Muted, wrap: true);
            statusText.Controls.Add(_statusValue, 0, 1);
            statusText.Controls.Add(_statusText, 0, 2);
            status.Controls.Add(_statusIcon, 0, 0);
            status.Controls.Add(statusText, 1, 0);
            sys.Controls.Add(status, 2, 0);

            var opt = Ui.Grid(1, 3);
            Ui.Rows(opt, (SizeType.Percent, 50), (SizeType.Absolute, 52), (SizeType.Percent, 50));
            var optimize = new AccentButton
            {
                Text = Get("dash.optimize_now"),
                Glyph = Glyphs.Bolt,
                HasDropDown = true,
                FontSize = 12f,
                Dock = DockStyle.Fill,
                Margin = new Padding(8, 0, 0, 0)
            };
            optimize.Click += (s, e) => _actions.OptimizeNow();
            optimize.DropDownClick += (s, e) => ShowOptimizeMenu(optimize);
            var optCaption = Ui.Text(Get("dash.optimize_caption"), UiFonts.Regular(9f), TextRole.Muted);
            optCaption.TextAlign = ContentAlignment.TopCenter;
            optCaption.Margin = new Padding(8, 6, 0, 0);
            opt.Controls.Add(optimize, 0, 1);
            opt.Controls.Add(optCaption, 0, 2);
            sys.Controls.Add(opt, 3, 0);

            sysCard.Controls.Add(sys);
            root.Controls.Add(sysCard, 0, 0);

            // ---------------- Metric tiles ----------------
            var tiles = Ui.Grid(4, 1);
            Ui.Columns(tiles, (SizeType.Percent, 25), (SizeType.Percent, 25), (SizeType.Percent, 25), (SizeType.Percent, 25));
            _cpuTile = new MetricTile(Glyphs.Cpu, Get("dash.cpu"), () => ThemeManager.Current.Accent, monitor.CpuHistory);
            _memTile = new MetricTile(Glyphs.Memory, Get("dash.memory"), () => ThemeManager.Current.Purple, monitor.MemoryHistory);
            _diskTile = new MetricTile(Glyphs.Disk, Get("dash.disk", SystemInfoService.SystemDriveRoot.TrimEnd('\\')), () => ThemeManager.Current.Success, monitor.DiskHistory);
            _gpuTile = new MetricTile(Glyphs.Gpu, Get("dash.gpu"), () => ThemeManager.Current.Warning, monitor.GpuHistory);
            _cpuTile.Margin = new Padding(0, 6, 6, 6);
            _memTile.Margin = new Padding(6, 6, 6, 6);
            _diskTile.Margin = new Padding(6, 6, 6, 6);
            _gpuTile.Margin = new Padding(6, 6, 0, 6);
            tiles.Controls.Add(_cpuTile, 0, 0);
            tiles.Controls.Add(_memTile, 1, 0);
            tiles.Controls.Add(_diskTile, 2, 0);
            tiles.Controls.Add(_gpuTile, 3, 0);
            root.Controls.Add(tiles, 0, 1);

            // ---------------- Quick actions ----------------
            var quickCard = Card();
            var quick = Ui.Grid(1, 2, new Padding(16, 10, 16, 14));
            Ui.Rows(quick, (SizeType.Absolute, 46), (SizeType.Percent, 100));
            quick.Controls.Add(CardHeader(Glyphs.Rocket, Get("dash.quick_actions"), Get("dash.quick_actions_sub"), null, null), 0, 0);
            var actionsGrid = Ui.Grid(5, 1);
            Ui.Columns(actionsGrid, (SizeType.Percent, 20), (SizeType.Percent, 20), (SizeType.Percent, 20), (SizeType.Percent, 20), (SizeType.Percent, 20));
            AddAction(actionsGrid, 0, Glyphs.Performance, Get("dash.qa_performance"), ColorTranslator.FromHtml("#1E6FE0"), PageIds.Performance);
            AddAction(actionsGrid, 1, Glyphs.Cleanup, Get("dash.qa_cleanup"), ColorTranslator.FromHtml("#14946B"), PageIds.Cleanup);
            AddAction(actionsGrid, 2, Glyphs.Privacy, Get("dash.qa_privacy"), ColorTranslator.FromHtml("#6E3AD8"), PageIds.Privacy);
            AddAction(actionsGrid, 3, Glyphs.Startup, Get("dash.qa_startup"), ColorTranslator.FromHtml("#C96A12"), PageIds.Startup);
            AddAction(actionsGrid, 4, Glyphs.Heart, Get("dash.qa_health"), ColorTranslator.FromHtml("#CC3A44"), PageIds.Health);
            quick.Controls.Add(actionsGrid, 0, 1);
            quickCard.Controls.Add(quick);
            root.Controls.Add(quickCard, 0, 2);

            // ---------------- Bottom: recent actions + disk space ----------------
            var bottom = Ui.Grid(2, 1);
            Ui.Columns(bottom, (SizeType.Percent, 55), (SizeType.Percent, 45));

            var recentCard = Card();
            recentCard.Margin = new Padding(0, 6, 6, 0);
            var recent = Ui.Grid(1, 2, new Padding(16, 10, 16, 10));
            Ui.Rows(recent, (SizeType.Absolute, 46), (SizeType.Percent, 100));
            recent.Controls.Add(CardHeader(Glyphs.Clock, Get("dash.recent_actions"), null, Get("dash.view_all"), _actions.ViewHistory), 0, 0);
            var recentBody = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            var recentList = Ui.Grid(1, RecentRows);
            recentList.Dock = DockStyle.Top;
            recentList.Height = RecentRows * 34;
            Ui.Rows(recentList, Enumerable.Range(0, RecentRows).Select(_ => (SizeType.Absolute, 34f)).ToArray());
            for (int i = 0; i < RecentRows; i++)
            {
                var row = Ui.Grid(3, 1);
                Ui.Columns(row, (SizeType.Absolute, 36), (SizeType.Percent, 100), (SizeType.Absolute, 150));
                var icon = new IconBadge { GlyphSize = 13f, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 8, 6), ShowBackground = false };
                var name = Ui.Text("", UiFonts.Regular(10f));
                var when = Ui.Text("", UiFonts.Regular(9f), TextRole.Muted);
                when.TextAlign = ContentAlignment.MiddleRight;
                row.Controls.Add(icon, 0, 0);
                row.Controls.Add(name, 1, 0);
                row.Controls.Add(when, 2, 0);
                _recent[i] = (icon, name, when);
                recentList.Controls.Add(row, 0, i);
            }
            _recentEmpty = Ui.Text(Get("dash.no_actions"), UiFonts.Regular(10f), TextRole.Muted, wrap: true);
            _recentEmpty.Padding = new Padding(0, 8, 0, 0);
            _recentEmpty.Visible = false;
            recentBody.Controls.Add(_recentEmpty);
            recentBody.Controls.Add(recentList);
            recent.Controls.Add(recentBody, 0, 1);
            recentCard.Controls.Add(recent);
            _recentList = recentList;
            bottom.Controls.Add(recentCard, 0, 0);

            var diskCard = Card();
            diskCard.Margin = new Padding(6, 6, 0, 0);
            var disk = Ui.Grid(1, DiskRows + 2, new Padding(16, 10, 16, 10));
            var diskRowStyles = new List<(SizeType, float)> { (SizeType.Absolute, 46) };
            for (int i = 0; i < DiskRows; i++) diskRowStyles.Add((SizeType.Absolute, 62));
            diskRowStyles.Add((SizeType.Percent, 100));
            Ui.Rows(disk, diskRowStyles.ToArray());
            disk.Controls.Add(CardHeader(Glyphs.Disk, Get("dash.disk_space"), null, Get("dash.see_details"),
                () => _actions.Navigate(PageIds.Health)), 0, 0);
            for (int i = 0; i < DiskRows; i++)
            {
                var row = Ui.Grid(3, 1);
                Ui.Columns(row, (SizeType.Absolute, 44), (SizeType.Percent, 100), (SizeType.Absolute, 58));
                var icon = new IconBadge { Glyph = Glyphs.Disk, GlyphSize = 16f, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 8, 12), ShowBackground = false };
                var mid = Ui.Grid(1, 3);
                Ui.Rows(mid, (SizeType.Absolute, 21), (SizeType.Absolute, 14), (SizeType.Percent, 100));
                var name = Ui.Text("", UiFonts.Semibold(10f));
                var bar = new UsageBar { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
                var used = Ui.Text("", UiFonts.Regular(9f), TextRole.Muted);
                mid.Controls.Add(name, 0, 0);
                mid.Controls.Add(bar, 0, 1);
                mid.Controls.Add(used, 0, 2);
                var pct = Ui.Text("", UiFonts.Semibold(11f));
                pct.TextAlign = ContentAlignment.MiddleRight;
                row.Controls.Add(icon, 0, 0);
                row.Controls.Add(mid, 1, 0);
                row.Controls.Add(pct, 2, 0);
                row.Visible = false;
                _disks[i] = (row, name, bar, used, pct);
                disk.Controls.Add(row, 0, i + 1);
            }
            diskCard.Controls.Add(disk);
            bottom.Controls.Add(diskCard, 1, 0);
            root.Controls.Add(bottom, 0, 3);

            Controls.Add(root);
            Ui.KeepMinScrollSize(this, new Size(900, 730));

            _history.Changed += OnHistoryChanged;
            _monitor.SampleReady += OnSample;
            VisibleChanged += (s, e) =>
            {
                if (!Visible) return;
                if (_monitor.Latest is MonitorSample latest) ApplySample(latest);
                RefreshDisks();
                RefreshRecent();
            };
        }

        #region Building blocks

        private static RoundedPanel Card() => new()
        {
            Kind = SurfaceKind.Card,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 0, 6)
        };

        private static Control CardHeader(string glyph, string title, string? subtitle, string? buttonText, Action? onButton)
        {
            var h = Ui.Grid(3, 1);
            Ui.Columns(h, (SizeType.Absolute, 40), (SizeType.Percent, 100), (SizeType.Absolute, buttonText == null ? 0 : 108));
            h.Controls.Add(new IconBadge { Glyph = glyph, GlyphSize = 15f, ShowBackground = false, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 6, 4) }, 0, 0);

            if (subtitle == null)
            {
                h.Controls.Add(Ui.Text(title, UiFonts.Semibold(12f)), 1, 0);
            }
            else
            {
                var t = Ui.Grid(1, 2);
                Ui.Rows(t, (SizeType.Percent, 55), (SizeType.Percent, 45));
                t.Controls.Add(Ui.Text(title, UiFonts.Semibold(12f)), 0, 0);
                t.Controls.Add(Ui.Text(subtitle, UiFonts.Regular(9f), TextRole.Muted), 0, 1);
                h.Controls.Add(t, 1, 0);
            }

            if (buttonText != null && onButton != null)
            {
                var b = new AccentButton { Text = buttonText, Secondary = true, FontSize = 9f, Dock = DockStyle.Fill, Margin = new Padding(6, 8, 0, 8) };
                b.Click += (s, e) => onButton();
                h.Controls.Add(b, 2, 0);
            }
            return h;
        }

        private void AddAction(TableLayoutPanel grid, int col, string glyph, string text, Color color, int page)
        {
            var tile = new ActionTile
            {
                Glyph = glyph,
                Text = text,
                BaseColor = color,
                Dock = DockStyle.Fill,
                Margin = new Padding(col == 0 ? 0 : 6, 2, col == 4 ? 0 : 6, 2)
            };
            tile.Click += (s, e) => _actions.Navigate(page);
            grid.Controls.Add(tile, col, 0);
        }

        private void ShowOptimizeMenu(Control anchor)
        {
            var menu = new ContextMenuStrip { RightToLeft = RightToLeft, ShowImageMargin = false };
            menu.Items.Add(Get("dash.menu_boost"), null, (s, e) => _actions.OptimizeNow());
            menu.Items.Add(Get("dash.menu_clean_temp"), null, (s, e) => _actions.CleanTempFiles());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Get("dash.menu_performance_page"), null, (s, e) => _actions.Navigate(PageIds.Performance));
            menu.Closed += (s, e) => BeginInvoke(menu.Dispose);
            menu.Show(anchor, new Point(0, anchor.Height));
        }

        #endregion

        #region Data

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                _info = await SystemInfoService.LoadStaticAsync();
                if (!IsDisposed) ShowStaticInfo();
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("Dashboard.LoadStatic", "SystemInfo", ex);
                _osTitle.Text = Get("dash.not_available");
            }
            if (_monitor.Latest is MonitorSample latest) ApplySample(latest);
            RefreshDisks();
            RefreshRecent();
        }

        private void ShowStaticInfo()
        {
            if (_info == null) return;
            string na = Get("dash.not_available");
            _osTitle.Text = _info.WindowsName;
            _osVersion.Text = Get("dash.version_line",
                string.IsNullOrEmpty(_info.DisplayVersion) ? na : Loc.Ltr(_info.DisplayVersion), Loc.Ltr(_info.Build));
            _osDevice.Text = Get("dash.device_line", _info.Model ?? _info.DeviceName);
            _osCpu.Text = Get("dash.cpu_line", _info.CpuName ?? na);
            _osRam.Text = Get("dash.ram_line", _info.TotalRamBytes > 0 ? Loc.Ltr(SystemInfoService.FormatBytes(_info.TotalRamBytes)) : na);
            _osGpu.Text = Get("dash.gpu_line", _info.Gpus.Count > 0 ? string.Join(", ", _info.Gpus) : na);
            if (_monitor.Latest is MonitorSample latest) ApplySample(latest);
        }

        /// <summary>Called on the monitor's background thread; hands the sample to the UI thread.</summary>
        private void OnSample(MonitorSample sample)
        {
            if (IsDisposed || !IsHandleCreated || !Visible) return;
            try { BeginInvoke(() => { if (!IsDisposed) ApplySample(sample); }); }
            catch (InvalidOperationException) { /* window closing */ }
        }

        private void ApplySample(MonitorSample s)
        {
            string unavailable = Get("dash.unavailable");
            int logical = _info?.LogicalProcessors ?? Environment.ProcessorCount;

            // CPU
            _cpuTile.SetValue(s.CpuPercent, s.CpuCurrentMhz is double mhz
                ? Get("dash.cpu_sub_freq", Loc.Ltr($"{mhz / 1000.0:0.00} GHz"), logical)
                : Get("dash.logical_processors", logical));
            _cpuTile.SetCaption(s.CpuPercent == null ? Get("dash.usage_unavailable") : "");

            // Memory: used / total and available
            _memTile.SetValue(s.MemoryPercent, s.MemoryUsedBytes is ulong used && s.MemoryTotalBytes is ulong total && s.MemoryAvailableBytes is ulong avail
                ? Get("dash.mem_sub", Loc.Ltr($"{SystemInfoService.FormatBytes(used)} / {SystemInfoService.FormatBytes(total)}"), Loc.Ltr(SystemInfoService.FormatBytes(avail)))
                : unavailable);
            _memTile.SetCaption("");

            // Disk: space used on the system drive; the chart shows disk activity
            _diskTile.SetValue(s.DiskUsedPercent, s.DiskUsedBytes is long dUsed && s.DiskTotalBytes is long dTotal
                ? Loc.Ltr($"{SystemInfoService.FormatBytes(dUsed)} / {SystemInfoService.FormatBytes(dTotal)}")
                : unavailable);
            _diskTile.SetCaption(s.DiskActivePercent is double act
                ? Get("dash.disk_activity", $"{act:0}")
                : Get("dash.disk_activity_unavailable"));

            // GPU: usage from GPU Engine counters when the driver provides them
            string gpuName = _info?.Gpus.Count > 0 ? _info.Gpus[0] : unavailable;
            _gpuTile.SetValue(s.GpuPercent, gpuName);
            _gpuTile.SetCaption(s.GpuPercent == null ? Get("dash.gpu_usage_unavailable") : "");

            _cpuTile.RefreshChart();
            _memTile.RefreshChart();
            _diskTile.RefreshChart();
            _gpuTile.RefreshChart();

            if ((DateTime.UtcNow - _lastDiskRefresh).TotalSeconds >= 30) RefreshDisks();
            UpdateStatus(s.DiskUsedPercent is double dp ? 100 - dp : null,
                s.MemoryPercent is double mp ? (uint)Math.Round(mp) : null);
        }

        /// <summary>Basic, real checks only. The text says which checks were made.</summary>
        private void UpdateStatus(double? sysFreePct, uint? memLoad)
        {
            var issues = new List<string>();
            if (sysFreePct is double f && f < 10) issues.Add(Get("dash.issue_disk", $"{f:0}"));
            if (memLoad is uint m && m >= 90) issues.Add(Get("dash.issue_memory", m));

            var p = ThemeManager.Current;
            if (issues.Count == 0)
            {
                _statusValue.Text = Get("dash.status_good");
                _statusValue.Role = TextRole.Success;
                _statusText.Text = Get("dash.status_good_desc");
                _statusIcon.Tint = p.Success;
            }
            else
            {
                _statusValue.Text = Get("dash.status_attention");
                _statusValue.Role = TextRole.Warning;
                _statusText.Text = string.Join(" ", issues);
                _statusIcon.Tint = p.Warning;
            }
            _statusIcon.Invalidate();
        }

        private void RefreshDisks()
        {
            _lastDiskRefresh = DateTime.UtcNow;
            var drives = SystemInfoService.GetFixedDrives();
            for (int i = 0; i < DiskRows; i++)
            {
                var row = _disks[i];
                if (i >= drives.Count) { row.Row.Visible = false; continue; }
                var d = drives[i];
                try
                {
                    long used = d.TotalSize - d.TotalFreeSpace;
                    double pct = used * 100.0 / d.TotalSize;
                    string label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? Get("dash.local_disk") : d.VolumeLabel;
                    row.Name.Text = $"{label} ({d.Name.TrimEnd('\\')})";
                    row.Bar.Percent = pct;
                    row.Bar.BarColor = pct >= 90 ? ThemeManager.Current.Warning : Color.Empty;
                    row.Used.Text = Get("dash.used_of", Loc.Ltr(SystemInfoService.FormatBytes(used)), Loc.Ltr(SystemInfoService.FormatBytes(d.TotalSize)));
                    row.Percent.Text = $"{pct:0}%";
                    row.Row.Visible = true;
                }
                catch
                {
                    row.Row.Visible = false;
                }
            }
        }

        private void OnHistoryChanged()
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke(RefreshRecent);
            else RefreshRecent();
        }

        private void RefreshRecent()
        {
            var items = _history.GetRecent(RecentRows);
            var p = ThemeManager.Current;
            CultureInfo culture;
            try { culture = CultureInfo.GetCultureInfo(Loc.CurrentLanguage); } catch { culture = CultureInfo.CurrentCulture; }

            for (int i = 0; i < RecentRows; i++)
            {
                var (icon, name, when) = _recent[i];
                bool show = i < items.Count;
                icon.Visible = name.Visible = when.Visible = show;
                if (!show) continue;

                var rec = items[i];
                (icon.Glyph, icon.Tint) = rec.Status switch
                {
                    OperationStatus.Completed => (Glyphs.CheckCircle, p.Success),
                    OperationStatus.Failed => (Glyphs.Error, p.Error),
                    _ => (Glyphs.Info, p.Accent)
                };
                icon.Invalidate();
                name.Text = rec.Name;
                when.Text = (rec.CompletedUtc ?? rec.StartedUtc).ToLocalTime().ToString("g", culture);
            }
            _recentEmpty.Visible = items.Count == 0;
            _recentList.Visible = items.Count > 0;
        }

        #endregion

        public void ApplyTheme(ThemePalette p)
        {
            BackColor = p.WindowBackground;
            ForeColor = p.Text;
            _statusIcon.Tint = _statusValue.Role == TextRole.Warning ? p.Warning : p.Success;
            RefreshRecent();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _monitor.SampleReady -= OnSample;
                _history.Changed -= OnHistoryChanged;
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Dashboard tile: icon, title, measured value (or "Unavailable"), details, ring gauge and a
    /// history chart fed by <see cref="SystemMonitorService"/>.
    /// </summary>
    public class MetricTile : RoundedPanel
    {
        private readonly Func<Color> _color;
        private readonly IconBadge _icon;
        private readonly ThemedLabel _value, _subtitle;
        private readonly RingGauge _ring;
        private readonly Sparkline _chart;

        public double? Value { get; private set; }

        public MetricTile(string glyph, string title, Func<Color> color, MetricHistory history)
        {
            _color = color;
            Dock = DockStyle.Fill;
            var grid = Ui.Grid(3, 4, new Padding(14, 10, 10, 8));
            Ui.Columns(grid, (SizeType.Absolute, 46), (SizeType.Percent, 100), (SizeType.Absolute, 70));
            Ui.Rows(grid, (SizeType.Absolute, 22), (SizeType.Absolute, 34), (SizeType.Absolute, 20), (SizeType.Percent, 100));

            _icon = new IconBadge { Glyph = glyph, GlyphSize = 18f, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
            grid.Controls.Add(_icon, 0, 0);
            grid.SetRowSpan(_icon, 2);

            grid.Controls.Add(Ui.Text(title, UiFonts.Semibold(10.5f)), 1, 0);
            _value = Ui.Text("—", UiFonts.Semibold(18f));
            _value.AutoEllipsis = false;
            grid.Controls.Add(_value, 1, 1);
            _subtitle = Ui.Text("", UiFonts.Regular(9f), TextRole.Muted);
            grid.Controls.Add(_subtitle, 0, 2);
            grid.SetColumnSpan(_subtitle, 3);

            _ring = new RingGauge { Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 0) };
            grid.Controls.Add(_ring, 2, 0);
            grid.SetRowSpan(_ring, 2);

            _chart = new Sparkline { Source = history, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
            grid.Controls.Add(_chart, 0, 3);
            grid.SetColumnSpan(_chart, 3);

            Controls.Add(grid);
        }

        /// <summary>Sets a measured value (0-100), or null to show "Unavailable".</summary>
        public void SetValue(double? percent, string? subtitle)
        {
            Value = percent;
            if (percent is double v)
            {
                _value.Text = $"{v:0}%";
                _value.Font = UiFonts.Semibold(18f);
                _value.Role = TextRole.Primary;
            }
            else
            {
                _value.Text = Loc.Get("dash.unavailable");
                _value.Font = UiFonts.Semibold(11f);
                _value.Role = TextRole.Muted;
            }
            _ring.Value = percent;
            if (subtitle != null) _subtitle.Text = subtitle;
        }

        public void SetSubtitle(string subtitle) => _subtitle.Text = subtitle;

        public void SetCaption(string caption)
        {
            if (_chart.Caption == caption) return;
            _chart.Caption = caption;
            _chart.Invalidate();
        }

        public void RefreshChart() => _chart.Invalidate();

        public override void ApplyTheme(ThemePalette p)
        {
            base.ApplyTheme(p);
            _icon.Tint = _color();
            _ring.RingColor = _color();
            _chart.LineColor = _color();
        }
    }
}
