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
    /// K-Win 1.5 window shell: sidebar navigation, top bar, page host and status bar.
    /// The 1.0 feature pages are hosted unchanged inside the new pages; they will be
    /// redesigned in later phases.
    /// </summary>
    public partial class MainForm
    {
        private SettingsService _settingsService = null!;

        private RoundedPanel _sidebar = null!;
        private RoundedPanel _mainArea = null!;
        private Panel _contentHost = null!;
        private readonly List<NavButton> _nav = new();
        private readonly Control?[] _pages = new Control?[PageIds.Count];
        private int _currentPage = -1;

        private IconBadge _pageIcon = null!;
        private ThemedLabel _pageTitle = null!, _pageSubtitle = null!;
        private ComboBox _themeCombo = null!, _langCombo = null!;
        private ThemedLabel _lastOptimizationLabel = null!;
        private Control _adminStatusControl = null!;
        private readonly List<Control> _legacyHosts = new();

        private DashboardPage? _dashboard;
        private SettingsPage? _settingsPage;
        private GroupBox? _startupGroup;
        private Label? _tempPreviewLabel;
        private bool _syncingSelectors;

        private static readonly (string Glyph, string Key)[] NavItems =
        {
            (Glyphs.Home, "dashboard"),
            (Glyphs.Performance, "performance"),
            (Glyphs.Cleanup, "cleanup"),
            (Glyphs.Privacy, "privacy"),
            (Glyphs.Startup, "startup"),
            (Glyphs.Health, "health"),
            (Glyphs.Tools, "tools"),
            (Glyphs.Settings, "settings"),
        };

        /// <summary>Builds the whole window. Called once from the constructor.</summary>
        private void BuildShell(int? initialPage)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            Text = DevMode.IsUiPreview ? $"{Get("app.title")} — {Get("dev.preview_title")}" : Get("app.title");
            Font = UiFonts.Regular(9.5f);
            var work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1320, (int)(work.Width * 0.92)), Math.Min(860, (int)(work.Height * 0.92)));
            MinimumSize = new Size(1024, 640);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;

            // Arabic: text reads right-to-left; the shell mirrors itself through its layouts.
            if (Loc.IsRTL) RightToLeft = RightToLeft.Yes;

            BuildSidebar();
            BuildMainArea();

            Controls.Add(_mainArea);
            Controls.Add(_sidebar);

            BuildPages();
            ResumeLayout(true);

            Navigate(initialPage is int p && p >= 0 && p < PageIds.Count ? p : PageIds.Dashboard);
            UpdateLastOptimization();
            _history.Changed += OnHistoryChangedForShell;
        }

        #region Sidebar

        private void BuildSidebar()
        {
            _sidebar = new RoundedPanel
            {
                Kind = SurfaceKind.Sidebar,
                Dock = Loc.IsRTL ? DockStyle.Right : DockStyle.Left,
                Width = 250,
                Padding = new Padding(12, 16, 12, 14)
            };

            var grid = Ui.Grid(1, 4);
            Ui.Rows(grid, (SizeType.Absolute, 104), (SizeType.Percent, 100), (SizeType.Absolute, 0), (SizeType.Absolute, 56));

            var brand = Ui.Grid(1, 3);
            Ui.Rows(brand, (SizeType.Absolute, 52), (SizeType.Absolute, 20), (SizeType.Absolute, 20));
            brand.Controls.Add(new LogoControl { Dock = DockStyle.Fill, Margin = new Padding(0) }, 0, 0);
            var line1 = Ui.Text(Get("app.subtitle"), UiFonts.Regular(10f));
            var line2 = Ui.Text(Get("shell.edition"), UiFonts.Regular(10f));
            line1.TextAlign = line2.TextAlign = ContentAlignment.MiddleCenter;
            brand.Controls.Add(line1, 0, 1);
            brand.Controls.Add(line2, 0, 2);
            grid.Controls.Add(brand, 0, 0);

            var navList = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Margin = new Padding(0, 10, 0, 0)
            };
            for (int i = 0; i < NavItems.Length; i++)
            {
                int index = i;
                var b = new NavButton
                {
                    Glyph = NavItems[i].Glyph,
                    Text = Get("nav." + NavItems[i].Key),
                    Width = 222,
                    Height = 46,
                    Margin = new Padding(0, 0, 0, 2),
                    TabIndex = i
                };
                b.Click += (s, e) => Navigate(index);
                _nav.Add(b);
                navList.Controls.Add(b);
            }
            navList.Resize += (s, e) =>
            {
                int w = navList.ClientSize.Width - navList.Padding.Horizontal;
                foreach (var b in _nav) b.Width = Math.Max(120, w);
            };
            grid.Controls.Add(navList, 0, 1);

            var license = new RoundedPanel { Kind = SurfaceKind.Card, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0) };
            var licGrid = Ui.Grid(2, 2, new Padding(8, 6, 8, 6));
            Ui.Columns(licGrid, (SizeType.Absolute, 40), (SizeType.Percent, 100));
            Ui.Rows(licGrid, (SizeType.Percent, 50), (SizeType.Percent, 50));
            var licIcon = new IconBadge { Glyph = Glyphs.Settings, GlyphSize = 15f, Tint = ThemeManager.Current.LogoYellow, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 6, 4) };
            licGrid.Controls.Add(licIcon, 0, 0);
            licGrid.SetRowSpan(licIcon, 2);
            licGrid.Controls.Add(Ui.Text(Get("shell.free_open_source"), UiFonts.Regular(9f)), 1, 0);
            licGrid.Controls.Add(Ui.Text(Get("shell.mit_license"), UiFonts.Regular(9f), TextRole.Muted), 1, 1);
            license.Controls.Add(licGrid);
            grid.Controls.Add(license, 0, 3);

            _sidebar.Controls.Add(grid);
        }

        #endregion

        #region Main area: top bar, content, status bar

        private void BuildMainArea()
        {
            _mainArea = new RoundedPanel { Kind = SurfaceKind.Window, Dock = DockStyle.Fill, Padding = new Padding(24, 14, 24, 0) };

            // ---- Top bar ----
            var top = Ui.Grid(2, 1);
            top.Dock = DockStyle.Top;
            top.Height = 76;
            Ui.Columns(top, (SizeType.Percent, 100), (SizeType.AutoSize, 0));

            var header = Ui.Grid(2, 2);
            Ui.Columns(header, (SizeType.Absolute, 56), (SizeType.Percent, 100));
            Ui.Rows(header, (SizeType.Percent, 58), (SizeType.Percent, 42));
            _pageIcon = new IconBadge { GlyphSize = 22f, ShowBackground = false, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 8, 6) };
            header.Controls.Add(_pageIcon, 0, 0);
            header.SetRowSpan(_pageIcon, 2);
            _pageTitle = Ui.Text("", UiFonts.Semibold(20f));
            _pageTitle.TextAlign = ContentAlignment.BottomLeft;
            _pageSubtitle = Ui.Text("", UiFonts.Regular(10f), TextRole.Muted);
            _pageSubtitle.TextAlign = ContentAlignment.TopLeft;
            header.Controls.Add(_pageTitle, 1, 0);
            header.Controls.Add(_pageSubtitle, 1, 1);
            top.Controls.Add(header, 0, 0);

            var tools = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 18, 0, 0),
                Margin = new Padding(12, 0, 0, 0)
            };
            StyleSecondaryButton(_undoButton);
            StyleSecondaryButton(_logViewerButton);
            _themeCombo = Selector(new[] { Get("shell.dark_mode"), Get("shell.light_mode") }, 150);
            _themeCombo.AccessibleName = Get("set.appearance");
            _themeCombo.SelectedIndexChanged += (s, e) =>
            {
                if (!_syncingSelectors) SetTheme(_themeCombo.SelectedIndex == 0);
            };
            var languages = Loc.AvailableLanguages.Where(l => l.Code is "en" or "ar" or "fr").ToList();
            _langCombo = Selector(languages.Select(l => l.Native).ToArray(), 140);
            _langCombo.AccessibleName = Get("set.language");
            _langCombo.Tag = languages.Select(l => l.Code).ToArray();
            _langCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_syncingSelectors || _langCombo.Tag is not string[] codes) return;
                string code = codes[_langCombo.SelectedIndex];
                if (code != Loc.CurrentLanguage) ChangeLanguage(code);
            };
            tools.Controls.Add(_undoButton);
            tools.Controls.Add(_logViewerButton);
            tools.Controls.Add(_themeCombo);
            tools.Controls.Add(_langCombo);
            top.Controls.Add(tools, 1, 0);

            // ---- Content ----
            _contentHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 10) };

            // ---- Status bar ----
            var status = BuildStatusBar();

            _mainArea.Controls.Add(_contentHost);
            _mainArea.Controls.Add(status);
            _mainArea.Controls.Add(top);
        }

        private Control BuildStatusBar()
        {
            var bar = Ui.Grid(3, 1);
            bar.Dock = DockStyle.Bottom;
            bar.Height = 42;
            bar.Margin = new Padding(0);
            Ui.Columns(bar, (SizeType.AutoSize, 0), (SizeType.Percent, 100), (SizeType.AutoSize, 0));

            var left = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, Padding = new Padding(0, 11, 0, 0), Margin = new Padding(0) };
            string[] parts = { "K-Win 1.5", Get("shell.edition"), Get("shell.mit_license") };
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) left.Controls.Add(StatusText("|", TextRole.Muted));
                left.Controls.Add(StatusText(parts[i], i == 1 ? TextRole.Primary : TextRole.Muted, i <= 1));
            }
            bar.Controls.Add(left, 0, 0);

            var middle = Ui.Grid(2, 1);
            Ui.Columns(middle, (SizeType.Percent, 100), (SizeType.Absolute, 130));
            middle.Margin = new Padding(16, 0, 16, 0);
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.AutoSize = false;
            _statusLabel.AutoEllipsis = true;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.Font = UiFonts.Regular(9f);
            _statusLabel.Margin = new Padding(0);
            _progressBar.Dock = DockStyle.None;
            _progressBar.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _progressBar.Height = 8;
            _progressBar.Visible = false;
            _progressBar.Margin = new Padding(8, 17, 0, 0);
            middle.Controls.Add(_statusLabel, 0, 0);
            middle.Controls.Add(_progressBar, 1, 0);
            bar.Controls.Add(middle, 1, 0);

            var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, Padding = new Padding(0, 11, 0, 0), Margin = new Padding(0) };
            if (DevMode.IsUiPreview)
            {
                var badge = StatusText(Get("dev.preview_badge"), TextRole.Warning, true);
                _undoToolTip.SetToolTip(badge, Get("dev.preview_banner"));
                right.Controls.Add(badge);
                right.Controls.Add(StatusText("|", TextRole.Muted));
            }
            if (ElevationHelper.IsAdministrator)
            {
                _adminStatusControl = StatusText(Glyphs.Admin, TextRole.Success, glyph: true);
                right.Controls.Add(_adminStatusControl);
                right.Controls.Add(StatusText(Get("shell.admin_yes"), TextRole.Success, true));
            }
            else
            {
                right.Controls.Add(StatusText(Glyphs.Admin, TextRole.Muted, glyph: true));
                var link = new LinkLabel
                {
                    Text = Get("shell.admin_no"),
                    AutoSize = true,
                    Font = UiFonts.Regular(9f),
                    Margin = new Padding(2, 0, 4, 0),
                    LinkBehavior = LinkBehavior.HoverUnderline
                };
                link.LinkClicked += (s, e) => RequestElevation(Get("elev.generic_op"));
                _undoToolTip.SetToolTip(link, Get("shell.admin_restart_tip"));
                _adminStatusControl = link;
                right.Controls.Add(link);
            }
            right.Controls.Add(StatusText("|", TextRole.Muted));
            right.Controls.Add(StatusText(Glyphs.Clock, TextRole.Muted, glyph: true));
            _lastOptimizationLabel = StatusText("", TextRole.Primary);
            right.Controls.Add(_lastOptimizationLabel);
            bar.Controls.Add(right, 2, 0);

            return bar;
        }

        private static ThemedLabel StatusText(string text, TextRole role, bool semibold = false, bool glyph = false) => new()
        {
            Text = text,
            Role = role,
            AutoSize = true,
            AutoEllipsis = false,
            Font = glyph ? Glyphs.Font(10f) : semibold ? UiFonts.Semibold(9f) : UiFonts.Regular(9f),
            Margin = new Padding(4, glyph ? 1 : 0, 4, 0)
        };

        private static ComboBox Selector(string[] items, int width)
        {
            var c = new ThemedComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Font = UiFonts.Regular(10f),
                Width = width,
                Margin = new Padding(8, 2, 0, 0)
            };
            c.Items.AddRange(items);
            return c;
        }

        private static void StyleSecondaryButton(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.Font = UiFonts.Regular(9.5f);
            b.Height = 30;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(8, 0, 8, 0);
            b.Margin = new Padding(8, 2, 0, 0);
            b.Cursor = Cursors.Hand;
            b.UseMnemonic = false;
        }

        #endregion

        #region Pages

        private void BuildPages()
        {
            _pages[PageIds.Dashboard] = _dashboard = new DashboardPage(new DashboardActions
            {
                Navigate = Navigate,
                OptimizeNow = () => _ = OneClickPerformanceBoostAsync(disableTransparency: true),
                CleanTempFiles = () =>
                {
                    if (_tempPreviewLabel != null) _ = CleanTempFilesAsync(_tempPreviewLabel);
                    else Navigate(PageIds.Cleanup);
                },
                ViewHistory = ShowHistory
            }, _history);

            // 1.0 pages, hosted unchanged (order matters: the Performance tab creates the startup group).
            _pages[PageIds.Performance] = HostLegacy(BuildPerformanceTab());
            _pages[PageIds.Privacy] = HostLegacy(BuildPrivacyTab());
            _pages[PageIds.Cleanup] = HostLegacy(BuildCleanupTab());
            _pages[PageIds.Startup] = BuildStartupPage();

            _pages[PageIds.Health] = new HealthPage(new HealthActions
            {
                RunSfc = RunSfcScanAsync,
                RunDism = RunDismCleanupAsync,
                OpenSystemProtection = OpenSystemProtection,
                Navigate = Navigate
            });

            _pages[PageIds.Tools] = BuildToolsPage();

            _pages[PageIds.Settings] = _settingsPage = new SettingsPage(new SettingsActions
            {
                SetDarkTheme = SetTheme,
                ChangeLanguage = ChangeLanguage,
                SetAutoRestorePoint = v =>
                {
                    _autoRestorePoint = v;
                    _settingsService.Current.AutoRestorePoint = v;
                    _settingsService.Save();
                },
                CleanOldBackups = days =>
                {
                    int n = _safety.CleanOldBackups(days);
                    Logger.Instance.CleanOldLogs(days);
                    SetStatus(Get("set.backups_cleaned", n), false);
                },
                OpenLogs = () => OpenFolder(Logger.Instance.LogDirectory),
                OpenBackups = () => OpenFolder(_safety.BackupDirectory),
                ViewHistory = ShowHistory
            }, _settingsService);

            foreach (var page in _pages)
            {
                if (page == null) continue;
                page.Dock = DockStyle.Fill;
                page.Visible = false;
                _contentHost.Controls.Add(page);
            }
        }

        /// <summary>Moves a 1.0 tab's content into a page container (mirrored in Arabic, as in 1.0).</summary>
        private Control HostLegacy(TabPage tab)
        {
            var host = new LegacyHostPanel { AutoScroll = true, Padding = new Padding(0) };
            while (tab.Controls.Count > 0)
            {
                var content = tab.Controls[0];
                // 1.0 pages were designed for a fixed 900x600 window: never squeeze them below
                // that size; the page scrolls instead of overlapping controls.
                content.MinimumSize = new Size(880, 500);
                host.Controls.Add(content);
            }
            tab.Dispose();
            _legacyHosts.Add(host);
            return host;
        }

        private Control BuildStartupPage()
        {
            var host = new LegacyHostPanel { AutoScroll = true, Padding = new Padding(0) };
            if (_startupGroup != null)
            {
                // Moved from the Performance page; Phase 5 replaces it with the full Startup Manager.
                _startupGroup.Parent?.Controls.Remove(_startupGroup);
                _startupGroup.Dock = DockStyle.None;
                _startupGroup.Location = new Point(0, 0);
                _startupGroup.Size = new Size(460, 200);
                host.Controls.Add(_startupGroup);
            }
            _legacyHosts.Add(host);
            return host;
        }

        private Control BuildToolsPage()
        {
            var page = new Panel();
            var tabs = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, WrapContents = false, Padding = new Padding(0, 0, 0, 8) };
            var body = new Panel { Dock = DockStyle.Fill };
            var sections = new (string Title, Control Content)[]
            {
                (Get("tools.tweaks"), HostLegacy(BuildTweaksTab())),
                (Get("tools.apps"), HostLegacy(BuildAppsTab())),
                (Get("tools.ram"), HostLegacy(BuildRamTab())),
            };
            var buttons = new List<NavButton>();
            for (int i = 0; i < sections.Length; i++)
            {
                int index = i;
                var content = sections[i].Content;
                content.Dock = DockStyle.Fill;
                content.Visible = i == 0;
                body.Controls.Add(content);

                var b = new NavButton { Text = sections[i].Title, Compact = true, Width = 170, Height = 40, IsActive = i == 0, Margin = new Padding(0, 0, 8, 0) };
                b.Click += (s, e) =>
                {
                    for (int j = 0; j < sections.Length; j++)
                    {
                        sections[j].Content.Visible = j == index;
                        buttons[j].IsActive = j == index;
                    }
                };
                buttons.Add(b);
                tabs.Controls.Add(b);
            }
            page.Controls.Add(body);
            page.Controls.Add(tabs);
            return page;
        }

        /// <summary>Shows one page and updates the sidebar and the top bar.</summary>
        private void Navigate(int index)
        {
            if (index < 0 || index >= PageIds.Count || _pages[index] == null) return;
            if (index == _currentPage) return;

            _contentHost.SuspendLayout();
            for (int i = 0; i < _pages.Length; i++)
            {
                if (_pages[i] != null) _pages[i]!.Visible = i == index;
                if (i < _nav.Count) _nav[i].IsActive = i == index;
            }
            _contentHost.ResumeLayout(true);
            _currentPage = index;

            // Always open a page at the top, and keep keyboard focus in the navigation.
            if (_pages[index] is ScrollableControl sc) sc.AutoScrollPosition = Point.Empty;
            if (ContainsFocus && index < _nav.Count) _nav[index].Focus();

            string key = NavItems[index].Key;
            _pageIcon.Glyph = NavItems[index].Glyph;
            _pageIcon.Invalidate();
            _pageTitle.Text = Get("nav." + key);
            _pageSubtitle.Text = Get("page." + key + "_sub");
        }

        #endregion

        #region Theme, language, misc actions

        private void SetTheme(bool dark)
        {
            _settingsService.Current.Theme = dark ? "dark" : "light";
            _settingsService.Save();
            _isDarkMode = dark;
            ThemeManager.SetTheme(dark);
            ApplyTheme();
        }

        private void SyncSelectors()
        {
            _syncingSelectors = true;
            try
            {
                _themeCombo.SelectedIndex = ThemeManager.Current.IsDark ? 0 : 1;
                if (_langCombo.Tag is string[] codes)
                    _langCombo.SelectedIndex = Math.Max(0, Array.IndexOf(codes, Loc.CurrentLanguage));
            }
            finally
            {
                _syncingSelectors = false;
            }
            _settingsPage?.SyncFromSettings();
        }

        /// <summary>Saves the new language and restarts K-Win (all text is built at startup).</summary>
        private void ChangeLanguage(string code)
        {
            var answer = MessageBox.Show(this, Get("shell.restart_language"), Get("set.language"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                SyncSelectors();
                return;
            }

            string previous = Loc.CurrentLanguage;
            Loc.SetLanguage(code);
            try
            {
                string? exe = Environment.ProcessPath;
                if (exe == null) throw new InvalidOperationException("Unknown executable path.");
                var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
                psi.ArgumentList.Add(ElevationHelper.TabArgument);
                psi.ArgumentList.Add(_currentPage.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (DevMode.IsUiPreview) psi.ArgumentList.Add(DevMode.Switch);
                Process.Start(psi)?.Dispose();
                Close();
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("ChangeLanguage", code, ex);
                Loc.SetLanguage(previous);
                SyncSelectors();
                SetStatus(Get("failed", ex.Message), true);
            }
        }

        private void ShowHistory()
        {
            using var form = new HistoryForm(_history);
            form.ShowDialog(this);
        }

        private void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                    Arguments = $"\"{path}\"",
                    UseShellExecute = false
                })?.Dispose();
            }
            catch (Exception ex)
            {
                SetStatus(Get("failed", ex.Message), true);
            }
        }

        private void OpenSystemProtection()
        {
            try
            {
                string exe = Path.Combine(Environment.SystemDirectory, "SystemPropertiesProtection.exe");
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true })?.Dispose();
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // The user declined the administrator prompt.
            }
            catch (Exception ex)
            {
                SetStatus(Get("failed", ex.Message), true);
            }
        }

        private void OnHistoryChangedForShell()
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke(UpdateLastOptimization);
            else UpdateLastOptimization();
        }

        private void UpdateLastOptimization()
        {
            var last = _history.LastSuccessful();
            string when;
            if (last == null)
            {
                when = Get("shell.never");
            }
            else
            {
                var t = (last.CompletedUtc ?? last.StartedUtc).ToLocalTime();
                System.Globalization.CultureInfo culture;
                try { culture = System.Globalization.CultureInfo.GetCultureInfo(Loc.CurrentLanguage); }
                catch { culture = System.Globalization.CultureInfo.CurrentCulture; }
                when = t.Date == DateTime.Today
                    ? Get("shell.today_at", t.ToString("t", culture))
                    : t.ToString("g", culture);
            }
            _lastOptimizationLabel.Text = Get("shell.last_optimization", when);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ThemeManager.ApplyTitleBar(this);
        }

        #endregion
    }
}
