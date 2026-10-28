using KWin.Controls;
using KWin.Services;
using KWin.Themes;
using KWin.Utils;
using static KWin.Utils.Loc;

namespace KWin.UI.Pages
{
    public sealed class HealthActions
    {
        public required Func<Task> RunSfc { get; init; }
        public required Func<Task> RunDism { get; init; }
        public required Action OpenSystemProtection { get; init; }
        public required Action<int> Navigate { get; init; }
    }

    /// <summary>
    /// System Health: real OS details, real storage figures with low-space warnings, and the existing
    /// SFC/DISM repair tools. Drive health (SMART) is not read in this version and is shown as Unavailable.
    /// This is a basic overview, not a complete health assessment, and the page says so.
    /// </summary>
    public class HealthPage : UserControl, IThemedControl
    {
        private readonly HealthActions _actions;
        private readonly TableLayoutPanel _osGrid;
        private readonly TableLayoutPanel _drivesGrid;
        private StaticSystemInfo? _info;

        public HealthPage(HealthActions actions)
        {
            _actions = actions;
            DoubleBuffered = true;
            AutoScroll = true;

            var root = Ui.Grid(2, 3);
            Ui.Columns(root, (SizeType.Percent, 50), (SizeType.Percent, 50));
            Ui.Rows(root, (SizeType.Absolute, 300), (SizeType.Absolute, 230), (SizeType.Percent, 100));

            // ---- Operating system ----
            var osCard = Card(new Padding(0, 6, 6, 6));
            var osBody = Ui.Grid(1, 2, new Padding(18, 12, 18, 12));
            Ui.Rows(osBody, (SizeType.Absolute, 40), (SizeType.Percent, 100));
            osBody.Controls.Add(Header(Glyphs.Pc, Get("health.os_title")), 0, 0);
            _osGrid = Ui.Grid(2, 7);
            Ui.Columns(_osGrid, (SizeType.Absolute, 150), (SizeType.Percent, 100));
            for (int i = 0; i < 7; i++) _osGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            osBody.Controls.Add(_osGrid, 0, 1);
            osCard.Controls.Add(osBody);
            root.Controls.Add(osCard, 0, 0);

            // ---- Storage ----
            var stCard = Card(new Padding(6, 6, 0, 6));
            var stBody = Ui.Grid(1, 3, new Padding(18, 12, 18, 12));
            Ui.Rows(stBody, (SizeType.Absolute, 40), (SizeType.Percent, 100), (SizeType.Absolute, 30));
            stBody.Controls.Add(Header(Glyphs.Disk, Get("health.storage_title")), 0, 0);
            _drivesGrid = Ui.Grid(1, 1);
            _drivesGrid.AutoScroll = true;
            stBody.Controls.Add(_drivesGrid, 0, 1);
            stBody.Controls.Add(Ui.Text(Get("health.smart_unavailable"), UiFonts.Regular(9f), TextRole.Muted), 0, 2);
            stCard.Controls.Add(stBody);
            root.Controls.Add(stCard, 1, 0);

            // ---- Repair tools ----
            var repairCard = Card(new Padding(0, 6, 0, 6));
            var repair = Ui.Grid(3, 2, new Padding(18, 12, 18, 14));
            Ui.Columns(repair, (SizeType.Percent, 33.3f), (SizeType.Percent, 33.3f), (SizeType.Percent, 33.4f));
            Ui.Rows(repair, (SizeType.Absolute, 40), (SizeType.Percent, 100));
            var repairHeader = Header(Glyphs.Tools, Get("health.repair_title"));
            repair.Controls.Add(repairHeader, 0, 0);
            repair.SetColumnSpan(repairHeader, 3);
            repair.Controls.Add(Tool(Get("health.sfc_title"), Get("health.sfc_desc"), Get("health.run"), async () => await _actions.RunSfc()), 0, 1);
            repair.Controls.Add(Tool(Get("health.dism_title"), Get("health.dism_desc"), Get("health.run"), async () => await _actions.RunDism()), 1, 1);
            repair.Controls.Add(Tool(Get("health.rp_title"), Get("health.rp_desc"), Get("health.open"), () => { _actions.OpenSystemProtection(); return Task.CompletedTask; }), 2, 1);
            repairCard.Controls.Add(repair);
            root.Controls.Add(repairCard, 0, 1);
            root.SetColumnSpan(repairCard, 2);

            // ---- Honest scope note ----
            var note = Ui.Text(Get("health.scope_note"), UiFonts.Regular(9f), TextRole.Muted, wrap: true);
            note.Margin = new Padding(4, 8, 4, 0);
            root.Controls.Add(note, 0, 2);
            root.SetColumnSpan(note, 2);

            Controls.Add(root);
            Ui.KeepMinScrollSize(this, new Size(860, 620));
            VisibleChanged += async (s, e) => { if (Visible) await RefreshDataAsync(); };
        }

        private static RoundedPanel Card(Padding margin) => new() { Kind = SurfaceKind.Card, Dock = DockStyle.Fill, Margin = margin };

        private static Control Header(string glyph, string title)
        {
            var h = Ui.Grid(2, 1);
            Ui.Columns(h, (SizeType.Absolute, 36), (SizeType.Percent, 100));
            h.Controls.Add(new IconBadge { Glyph = glyph, GlyphSize = 15f, ShowBackground = false, Dock = DockStyle.Fill }, 0, 0);
            h.Controls.Add(Ui.Text(title, UiFonts.Semibold(12f)), 1, 0);
            return h;
        }

        private static Control Tool(string title, string desc, string button, Func<Task> run)
        {
            var t = Ui.Grid(1, 3);
            t.Margin = new Padding(0, 4, 16, 0);
            Ui.Rows(t, (SizeType.Absolute, 28), (SizeType.Percent, 100), (SizeType.Absolute, 40));
            t.Controls.Add(Ui.Text(title, UiFonts.Semibold(10.5f)), 0, 0);
            t.Controls.Add(Ui.Text(desc, UiFonts.Regular(9f), TextRole.Muted, wrap: true), 0, 1);
            var b = new AccentButton { Text = button, FontSize = 9.5f, Dock = DockStyle.Left, Width = 140, Margin = new Padding(0, 4, 0, 2) };
            b.Click += async (s, e) =>
            {
                try { await run(); }
                catch (Exception ex) { Logger.Instance.Error("HealthPage.Tool", title, ex); }
            };
            t.Controls.Add(b, 0, 2);
            return t;
        }

        private async Task RefreshDataAsync()
        {
            try { _info ??= await SystemInfoService.LoadStaticAsync(); }
            catch (Exception ex) { Logger.Instance.Error("HealthPage", "SystemInfo", ex); }
            if (IsDisposed || _info == null) return;
            var info = _info;
            string na = Get("dash.not_available");
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

            var rows = new (string Key, string Value)[]
            {
                (Get("health.edition"), info.WindowsName),
                (Get("health.version"), string.IsNullOrEmpty(info.DisplayVersion) ? na : info.DisplayVersion),
                (Get("health.build"), info.Build),
                (Get("health.architecture"), info.Architecture),
                (Get("health.device"), info.Model ?? info.DeviceName),
                (Get("health.uptime"), Get("health.uptime_value", (int)uptime.TotalDays, uptime.Hours, uptime.Minutes)),
                (Get("health.admin"), ElevationHelper.IsAdministrator ? Get("word.yes") : Get("word.no"))
            };

            _osGrid.SuspendLayout();
            foreach (Control c in _osGrid.Controls.Cast<Control>().ToList()) { _osGrid.Controls.Remove(c); c.Dispose(); }
            for (int i = 0; i < rows.Length; i++)
            {
                _osGrid.Controls.Add(Ui.Text(rows[i].Key, UiFonts.Regular(9.5f), TextRole.Muted), 0, i);
                _osGrid.Controls.Add(Ui.Text(rows[i].Value, UiFonts.Regular(9.5f)), 1, i);
            }
            _osGrid.ResumeLayout();

            _drivesGrid.SuspendLayout();
            foreach (Control c in _drivesGrid.Controls.Cast<Control>().ToList()) { _drivesGrid.Controls.Remove(c); c.Dispose(); }
            _drivesGrid.RowStyles.Clear();
            var drives = SystemInfoService.GetFixedDrives();
            _drivesGrid.RowCount = Math.Max(1, drives.Count);
            for (int i = 0; i < drives.Count; i++)
            {
                var d = drives[i];
                _drivesGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
                var row = Ui.Grid(1, 3);
                Ui.Rows(row, (SizeType.Absolute, 22), (SizeType.Absolute, 14), (SizeType.Absolute, 22));
                try
                {
                    long free = d.TotalFreeSpace;
                    double usedPct = (d.TotalSize - free) * 100.0 / d.TotalSize;
                    double freePct = 100 - usedPct;
                    string label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? Get("dash.local_disk") : d.VolumeLabel;
                    row.Controls.Add(Ui.Text($"{label} ({d.Name.TrimEnd('\\')}) · {d.DriveFormat}", UiFonts.Semibold(10f)), 0, 0);
                    row.Controls.Add(new UsageBar
                    {
                        Percent = usedPct,
                        BarColor = freePct < 10 ? ThemeManager.Current.Warning : Color.Empty,
                        Dock = DockStyle.Fill,
                        Margin = new Padding(0, 2, 0, 2)
                    }, 0, 1);
                    var detail = Ui.Text(Get("health.free_of", Loc.Ltr(SystemInfoService.FormatBytes(free)), Loc.Ltr(SystemInfoService.FormatBytes(d.TotalSize)), $"{freePct:0}"),
                        UiFonts.Regular(9f), freePct < 10 ? TextRole.Warning : TextRole.Muted);
                    if (freePct < 10) detail.Text += " · " + Get("health.low_space");
                    row.Controls.Add(detail, 0, 2);
                }
                catch
                {
                    row.Controls.Add(Ui.Text($"{d.Name} — {Get("dash.not_available")}", UiFonts.Regular(9.5f), TextRole.Muted), 0, 0);
                }
                _drivesGrid.Controls.Add(row, 0, i);
            }
            _drivesGrid.ResumeLayout();
            ThemeManager.ApplyTree(this);
        }

        public void ApplyTheme(ThemePalette p)
        {
            BackColor = p.WindowBackground;
            ForeColor = p.Text;
        }
    }
}
