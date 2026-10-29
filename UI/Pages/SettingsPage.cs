using System.Diagnostics;
using KWin.Controls;
using KWin.Services;
using KWin.Themes;
using KWin.Utils;
using static KWin.Utils.Loc;

namespace KWin.UI.Pages
{
    public sealed class SettingsActions
    {
        public required Action<bool> SetDarkTheme { get; init; }
        public required Action<string> ChangeLanguage { get; init; }
        public required Action<bool> SetAutoRestorePoint { get; init; }
        public required Action<int> CleanOldBackups { get; init; }
        public required Action OpenLogs { get; init; }
        public required Action OpenBackups { get; init; }
        public required Action ViewHistory { get; init; }
        public required Action<int> SetMonitorInterval { get; init; }
        public required Func<Task> CheckForUpdates { get; init; }
    }

    /// <summary>
    /// Settings: theme, language, safety options, data folders and about/license.
    /// Everything is stored locally (settings.json / language.json); no account, no cloud.
    /// </summary>
    public class SettingsPage : UserControl, IThemedControl
    {
        private const string RepoUrl = "https://github.com/Elomami1976/K-Win";

        private readonly SettingsActions _actions;
        private readonly SettingsService _settings;
        private readonly RadioButton _dark, _light;
        private readonly ThemedComboBox _language;
        private readonly CheckBox _restorePoint;
        private readonly NumericUpDown _retention;
        private readonly ThemedComboBox _interval;
        private readonly CheckBox _updatesOnStart;
        private readonly ThemedLabel _lastCheck;
        private bool _syncing;

        public SettingsPage(SettingsActions actions, SettingsService settings)
        {
            _actions = actions;
            _settings = settings;
            DoubleBuffered = true;
            AutoScroll = true;

            var root = Ui.Grid(2, 4);
            Ui.Columns(root, (SizeType.Percent, 50), (SizeType.Percent, 50));
            Ui.Rows(root, (SizeType.Absolute, 200), (SizeType.Absolute, 230), (SizeType.Absolute, 230), (SizeType.Absolute, 210));

            // ---- Appearance ----
            var appearance = Section(Glyphs.Moon, Get("set.appearance"), new Padding(0, 6, 6, 6), out var aBody);
            _dark = Radio(Get("set.dark"));
            _light = Radio(Get("set.light"));
            _dark.CheckedChanged += (s, e) => { if (!_syncing && _dark.Checked) _actions.SetDarkTheme(true); };
            _light.CheckedChanged += (s, e) => { if (!_syncing && _light.Checked) _actions.SetDarkTheme(false); };
            aBody.Controls.Add(_dark, 0, 0);
            aBody.Controls.Add(_light, 0, 1);
            aBody.Controls.Add(Ui.Text(Get("set.theme_note"), UiFonts.Regular(9f), TextRole.Muted, wrap: true), 0, 2);
            root.Controls.Add(appearance, 0, 0);

            // ---- Language ----
            var language = Section(Glyphs.Globe, Get("set.language"), new Padding(6, 6, 0, 6), out var lBody);
            _language = new ThemedComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Font = UiFonts.Regular(10f),
                Width = 240,
                Margin = new Padding(0, 6, 0, 6)
            };
            foreach (var (code, native, _) in Loc.AvailableLanguages.Where(l => l.Code is "en" or "ar" or "fr"))
                _language.Items.Add(new LanguageItem(code, native));
            _language.SelectedIndexChanged += (s, e) =>
            {
                if (!_syncing && _language.SelectedItem is LanguageItem li && li.Code != Loc.CurrentLanguage)
                    _actions.ChangeLanguage(li.Code);
            };
            lBody.Controls.Add(_language, 0, 0);
            lBody.Controls.Add(Ui.Text(Get("set.language_note"), UiFonts.Regular(9f), TextRole.Muted, wrap: true), 0, 1);
            root.Controls.Add(language, 1, 0);

            // ---- Safety ----
            var safety = Section(Glyphs.Privacy, Get("set.safety"), new Padding(0, 6, 6, 6), out var sBody);
            _restorePoint = new CheckBox { Text = Get("set.restore_point"), AutoSize = true, Font = UiFonts.Regular(10f), Margin = new Padding(0, 6, 0, 2) };
            _restorePoint.CheckedChanged += (s, e) => { if (!_syncing) _actions.SetAutoRestorePoint(_restorePoint.Checked); };
            sBody.Controls.Add(_restorePoint, 0, 0);

            var retentionRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            retentionRow.Controls.Add(new ThemedLabel { Text = Get("set.retention"), AutoSize = true, Font = UiFonts.Regular(10f), Margin = new Padding(0, 6, 8, 0), AutoEllipsis = false });
            _retention = new NumericUpDown { Minimum = 1, Maximum = 365, Width = 70, Font = UiFonts.Regular(10f) };
            _retention.ValueChanged += (s, e) =>
            {
                if (_syncing) return;
                _settings.Current.BackupRetentionDays = (int)_retention.Value;
                _settings.Save();
            };
            retentionRow.Controls.Add(_retention);
            var cleanBtn = new AccentButton { Text = Get("set.clean_backups"), Secondary = true, FontSize = 9f, Width = 190, Height = 30, Margin = new Padding(12, 0, 0, 4) };
            cleanBtn.Click += (s, e) => _actions.CleanOldBackups((int)_retention.Value);
            retentionRow.Controls.Add(cleanBtn);
            sBody.Controls.Add(retentionRow, 0, 1);
            sBody.Controls.Add(Ui.Text(Get("set.safety_note"), UiFonts.Regular(9f), TextRole.Muted, wrap: true), 0, 2);
            root.Controls.Add(safety, 0, 1);

            // ---- Data & logs ----
            var data = Section(Glyphs.Document, Get("set.data"), new Padding(6, 6, 0, 6), out var dBody);
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0) };
            buttons.Controls.Add(Button(Get("set.open_logs"), _actions.OpenLogs));
            buttons.Controls.Add(Button(Get("set.open_backups"), _actions.OpenBackups));
            buttons.Controls.Add(Button(Get("set.view_history"), _actions.ViewHistory));
            dBody.Controls.Add(buttons, 0, 0);
            dBody.SetRowSpan(buttons, 2);
            dBody.Controls.Add(Ui.Text(Get("set.data_note", AppPaths.DataDirectory), UiFonts.Regular(9f), TextRole.Muted, wrap: true), 0, 2);
            root.Controls.Add(data, 1, 1);

            // ---- About ----
            // ---- Monitoring ----
            var monitoring = Section(Glyphs.Cpu, Get("set.monitoring"), new Padding(0, 6, 6, 6), out var mBody);
            _interval = new ThemedComboBox { Font = UiFonts.Regular(10f), Width = 200, Margin = new Padding(0, 6, 0, 6) };
            foreach (int sec in SystemMonitorService.AllowedIntervalsSeconds)
                _interval.Items.Add(new IntervalItem(sec, Get("set.every_seconds", sec)));
            _interval.SelectedIndexChanged += (s, e) =>
            {
                if (!_syncing && _interval.SelectedItem is IntervalItem it) _actions.SetMonitorInterval(it.Seconds);
            };
            mBody.Controls.Add(Ui.Text(Get("set.interval"), UiFonts.Regular(10f)), 0, 0);
            mBody.Controls.Add(_interval, 0, 1);
            mBody.Controls.Add(Ui.Text(Get("set.monitoring_note"), UiFonts.Regular(9f), TextRole.Muted, wrap: true), 0, 2);
            root.Controls.Add(monitoring, 0, 2);

            var about = Section(Glyphs.Info, Get("set.about"), new Padding(6, 6, 0, 6), out var abBody);
            string version = Application.ProductVersion.Split('+')[0];
            abBody.Controls.Add(Ui.Text(Get("set.about_line", version), UiFonts.Semibold(10.5f)), 0, 0);
            abBody.Controls.Add(Ui.Text(Get("set.license_line"), UiFonts.Regular(9.5f), TextRole.Muted, wrap: true), 0, 1);
            var link = new LinkLabel { Text = RepoUrl, AutoSize = true, Font = UiFonts.Regular(9.5f), Margin = new Padding(0, 4, 0, 0) };
            link.LinkClicked += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true }); }
                catch (Exception ex) { Logger.Instance.Error("OpenRepo", RepoUrl, ex); }
            };
            abBody.Controls.Add(link, 0, 2);
            if (DevMode.IsUiPreview)
            {
                var dev = Ui.Text(Get("dev.preview_banner"), UiFonts.Semibold(9.5f), TextRole.Warning);
                abBody.RowCount = 4;
                abBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
                abBody.Controls.Add(dev, 0, 3);
            }
            root.Controls.Add(about, 1, 2);

            // ---- Updates (optional, off by default) ----
            var updates = Section(Glyphs.Globe, Get("upd.title"), new Padding(0, 6, 0, 6), out var uBody);
            _updatesOnStart = new CheckBox { Text = Get("upd.on_start"), AutoSize = true, Font = UiFonts.Regular(10f), Margin = new Padding(0, 6, 0, 2) };
            _updatesOnStart.CheckedChanged += (s, e) =>
            {
                if (_syncing) return;
                _settings.Current.CheckUpdatesOnStart = _updatesOnStart.Checked;
                _settings.Save();
            };
            uBody.Controls.Add(_updatesOnStart, 0, 0);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0) };
            var check = Button(Get("upd.check_now"), () => _ = _actions.CheckForUpdates());
            row.Controls.Add(check);
            _lastCheck = new ThemedLabel { AutoSize = true, AutoEllipsis = false, Role = TextRole.Muted, Font = UiFonts.Regular(9.5f), Margin = new Padding(4, 8, 0, 0) };
            row.Controls.Add(_lastCheck);
            uBody.Controls.Add(row, 0, 1);
            uBody.Controls.Add(Ui.Text(Get("upd.note"), UiFonts.Regular(9.5f), TextRole.Muted, wrap: true), 0, 2);
            root.Controls.Add(updates, 0, 3);
            root.SetColumnSpan(updates, 2);

            Controls.Add(root);
            Ui.KeepMinScrollSize(this, new Size(820, 900));
            SyncFromSettings();
        }

        private sealed record IntervalItem(int Seconds, string Name)
        {
            public override string ToString() => Name;
        }

        private sealed record LanguageItem(string Code, string Name)
        {
            public override string ToString() => Name;
        }

        private static RoundedPanel Section(string glyph, string title, Padding margin, out TableLayoutPanel body)
        {
            var card = new RoundedPanel { Kind = SurfaceKind.Card, Dock = DockStyle.Fill, Margin = margin };
            var grid = Ui.Grid(1, 2, new Padding(18, 12, 18, 12));
            Ui.Rows(grid, (SizeType.Absolute, 40), (SizeType.Percent, 100));
            var h = Ui.Grid(2, 1);
            Ui.Columns(h, (SizeType.Absolute, 36), (SizeType.Percent, 100));
            h.Controls.Add(new IconBadge { Glyph = glyph, GlyphSize = 15f, ShowBackground = false, Dock = DockStyle.Fill }, 0, 0);
            h.Controls.Add(Ui.Text(title, UiFonts.Semibold(12f)), 1, 0);
            grid.Controls.Add(h, 0, 0);
            body = Ui.Grid(1, 3);
            Ui.Rows(body, (SizeType.AutoSize, 0), (SizeType.AutoSize, 0), (SizeType.Percent, 100));
            grid.Controls.Add(body, 0, 1);
            card.Controls.Add(grid);
            return card;
        }

        private static RadioButton Radio(string text) => new()
        {
            Text = text,
            AutoSize = true,
            Font = UiFonts.Regular(10f),
            Margin = new Padding(0, 6, 0, 2)
        };

        private static AccentButton Button(string text, Action onClick)
        {
            var b = new AccentButton { Text = text, Secondary = true, FontSize = 9.5f, Width = 190, Height = 34, Margin = new Padding(0, 0, 10, 10) };
            b.Click += (s, e) => onClick();
            return b;
        }

        /// <summary>Updates the controls from the saved settings (e.g. after the theme changed in the top bar).</summary>
        public void SyncFromSettings()
        {
            _syncing = true;
            try
            {
                _dark.Checked = _settings.Current.IsDark;
                _light.Checked = !_settings.Current.IsDark;
                _restorePoint.Checked = _settings.Current.AutoRestorePoint;
                _retention.Value = Math.Clamp(_settings.Current.BackupRetentionDays, 1, 365);
                _updatesOnStart.Checked = _settings.Current.CheckUpdatesOnStart;
                _lastCheck.Text = _settings.Current.LastUpdateCheckUtc is DateTime t
                    ? Get("upd.last_check", t.ToLocalTime().ToString("g", Loc.Culture))
                    : Get("upd.never_checked");
                _interval.SelectedItem = _interval.Items.Cast<IntervalItem>()
                    .FirstOrDefault(i => i.Seconds == _settings.Current.MonitorIntervalSeconds);
                _language.SelectedItem = _language.Items.Cast<LanguageItem>().FirstOrDefault(i => i.Code == Loc.CurrentLanguage)
                                         ?? _language.Items.Cast<LanguageItem>().FirstOrDefault();
            }
            finally
            {
                _syncing = false;
            }
        }

        public void ApplyTheme(ThemePalette p)
        {
            BackColor = p.WindowBackground;
            ForeColor = p.Text;
            foreach (var c in new Control[] { _dark, _light, _restorePoint, _updatesOnStart })
                c.ForeColor = p.Text;
            foreach (var c in new Control[] { _language, _retention })
            {
                c.BackColor = p.Input;
                c.ForeColor = p.Text;
            }
            ApplyLinks(this, p);
        }

        private static void ApplyLinks(Control root, ThemePalette p)
        {
            foreach (Control c in root.Controls)
            {
                if (c is LinkLabel l)
                {
                    l.LinkColor = p.Accent;
                    l.ActiveLinkColor = p.LogoYellow;
                    l.VisitedLinkColor = p.Accent;
                }
                ApplyLinks(c, p);
            }
        }
    }
}
