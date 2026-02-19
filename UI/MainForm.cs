using Microsoft.Win32;
using KWin.Core;
using KWin.Models;
using KWin.Utils;
using static KWin.Utils.Loc;

namespace KWin.UI
{
    /// <summary>
    /// Main application window with TabControl hosting Performance, Privacy, and Cleanup tabs.
    /// 900x600px fixed size, Windows 11 themed, fully async operations.
    /// </summary>
    public class MainForm : Form
    {
        // Core services
        private readonly SafetyEngine _safety;
        private readonly RegistryHelper _registry;
        private readonly SystemTools _systemTools;
        private readonly CleanupManager _cleanup;
        private readonly AppManager _appManager;

        // RAM monitor timer
        private System.Windows.Forms.Timer? _ramTimer;

        // UI components
        private readonly TabControl _tabControl;
        private readonly ProgressBar _progressBar;
        private readonly Label _statusLabel;
        private readonly Button _undoButton;
        private readonly Button _settingsButton;
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

        public MainForm()
        {
            // Initialize core services
            _safety = new SafetyEngine();
            _registry = new RegistryHelper();
            _systemTools = new SystemTools();
            _cleanup = new CleanupManager(_systemTools);
            _appManager = new AppManager();

            // Detect theme
            _isDarkMode = SettingsForm.IsWindowsDarkMode();

            // Form setup
            Text = Get("app.title");
            Size = new Size(920, 620);
            MinimumSize = new Size(920, 620);
            MaximumSize = new Size(920, 620);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Font = IsFontInstalled("Segoe UI Variable")
                ? new Font("Segoe UI Variable", 9.5F)
                : new Font("Segoe UI", 9.5F);

            // Header panel
            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 55,
                BackColor = AccentBlue
            };

            var titleLabel = new Label
            {
                Text = Get("app.name"),
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(15, 10),
                AutoSize = true
            };

            var subtitleLabel = new Label
            {
                Text = Get("app.subtitle"),
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(200, 220, 255),
                Location = new Point(100, 22),
                AutoSize = true
            };

            _settingsButton = new Button
            {
                Text = Get("app.settings"),
                Size = new Size(90, 30),
                Location = new Point(800, 12),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(0, 100, 180),
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9F)
            };
            _settingsButton.FlatAppearance.BorderColor = Color.FromArgb(0, 100, 180);
            _settingsButton.Click += SettingsButton_Click;

            headerPanel.Controls.Add(titleLabel);
            headerPanel.Controls.Add(subtitleLabel);
            headerPanel.Controls.Add(_settingsButton);

            // Tab control
            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10F),
                Padding = new Point(15, 5)
            };

            // Footer panel
            var footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 55
            };

            _progressBar = new ProgressBar
            {
                Location = new Point(15, 8),
                Size = new Size(450, 18),
                Style = ProgressBarStyle.Continuous
            };

            _statusLabel = new Label
            {
                Text = Get("app.ready"),
                Location = new Point(15, 30),
                Size = new Size(450, 20),
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.Gray
            };

            _undoButton = new Button
            {
                Text = Get("app.undo"),
                Size = new Size(85, 32),
                Location = new Point(640, 10),
                FlatStyle = FlatStyle.Flat,
                Enabled = false,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9F)
            };
            _undoButton.Click += UndoButton_Click;

            _logViewerButton = new Button
            {
                Text = Get("app.logs"),
                Size = new Size(80, 32),
                Location = new Point(735, 10),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9F)
            };
            _logViewerButton.Click += LogViewerButton_Click;

            var winVersionLabel = new Label
            {
                Text = WindowsVersionChecker.GetVersionString(),
                Location = new Point(825, 18),
                Size = new Size(75, 32),
                Font = new Font("Segoe UI", 7.5F),
                ForeColor = Color.Gray,
                TextAlign = ContentAlignment.MiddleRight
            };

            footerPanel.Controls.Add(_progressBar);
            footerPanel.Controls.Add(_statusLabel);
            footerPanel.Controls.Add(_undoButton);
            footerPanel.Controls.Add(_logViewerButton);
            footerPanel.Controls.Add(winVersionLabel);

            // Build tabs
            _tabControl.TabPages.Add(BuildPerformanceTab());
            _tabControl.TabPages.Add(BuildPrivacyTab());
            _tabControl.TabPages.Add(BuildCleanupTab());
            _tabControl.TabPages.Add(BuildTweaksTab());
            _tabControl.TabPages.Add(BuildAppsTab());
            _tabControl.TabPages.Add(BuildRamTab());

            // Add controls (order matters for DockStyle)
            Controls.Add(_tabControl);
            Controls.Add(footerPanel);
            Controls.Add(headerPanel);

            // Apply RTL if Arabic
            if (Loc.IsRTL)
            {
                RightToLeft = RightToLeft.Yes;
                RightToLeftLayout = true;
            }

            // Apply theme
            ApplyTheme();

            // Log startup
            Logger.Instance.Info("Application", "MainForm", "started",
                newValue: WindowsVersionChecker.GetVersionString());
        }

        #region Performance Tab

        private TabPage BuildPerformanceTab()
        {
            var tab = new TabPage(Get("tab.performance"));
            tab.Padding = new Padding(10);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                AutoScroll = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));

            // Description panel (right side)
            var descPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            var descLabel = new Label
            {
                Text = Get("perf.desc_default"),
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.Gray
            };
            descPanel.Controls.Add(descLabel);

            // --- Power Plan Selector ---
            var powerGroup = new GroupBox
            {
                Text = Get("perf.power_plan"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var powerCombo = new ComboBox
            {
                Location = new Point(15, 30),
                Size = new Size(250, 28),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular)
            };

            var powerApplyBtn = new Button
            {
                Text = Get("common.apply"),
                Location = new Point(275, 28),
                Size = new Size(70, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            powerApplyBtn.FlatAppearance.BorderSize = 0;

            var powerNote = new Label
            {
                Text = Get("perf.power_note"),
                Location = new Point(15, 65),
                Size = new Size(400, 20),
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.DarkOrange
            };

            powerGroup.Controls.Add(powerCombo);
            powerGroup.Controls.Add(powerApplyBtn);
            powerGroup.Controls.Add(powerNote);

            var createUltimatePerfBtn = new Button
            {
                Text = Get("perf.create_ultimate"),
                Location = new Point(15, 90),
                Size = new Size(250, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 150, 80),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5F),
                Cursor = Cursors.Hand
            };
            createUltimatePerfBtn.FlatAppearance.BorderSize = 0;
            createUltimatePerfBtn.Click += async (s, e) => await CreateUltimatePerfPlanAsync(powerCombo);

            powerGroup.Controls.Add(createUltimatePerfBtn);

            // Load power plans async
            LoadPowerPlansAsync(powerCombo);

            powerApplyBtn.Click += async (s, e) =>
            {
                if (powerCombo.SelectedItem is PowerPlanItem plan)
                    await ApplyPowerPlanAsync(plan.Guid);
            };

            // --- Visual Effects ---
            var visualGroup = new GroupBox
            {
                Text = Get("perf.visual_effects"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var visualBestPerfBtn = new Button
            {
                Text = Get("perf.best_perf"),
                Location = new Point(15, 30),
                Size = new Size(280, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            visualBestPerfBtn.FlatAppearance.BorderSize = 0;

            var disableTransparencyCheck = new CheckBox
            {
                Text = Get("perf.also_disable_transparency"),
                Location = new Point(15, 72),
                Size = new Size(300, 24),
                Checked = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var disableAnimationsCheck = new CheckBox
            {
                Text = Get("perf.disable_animations"),
                Location = new Point(15, 96),
                Size = new Size(300, 24),
                Checked = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var disableShadowsCheck = new CheckBox
            {
                Text = Get("perf.disable_shadows"),
                Location = new Point(15, 118),
                Size = new Size(300, 24),
                Checked = false,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            visualBestPerfBtn.Click += async (s, e) => await ApplyVisualEffectsOptimizeAsync(
                disableTransparencyCheck.Checked, disableAnimationsCheck.Checked, disableShadowsCheck.Checked);

            var restoreVisualBtn = new Button
            {
                Text = Get("perf.restore_visual"),
                Location = new Point(15, 148),
                Size = new Size(250, 28),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F),
                Cursor = Cursors.Hand
            };
            restoreVisualBtn.Click += async (s, e) => await RestoreDefaultVisualEffectsAsync();

            visualGroup.Controls.Add(visualBestPerfBtn);
            visualGroup.Controls.Add(disableTransparencyCheck);
            visualGroup.Controls.Add(disableAnimationsCheck);
            visualGroup.Controls.Add(disableShadowsCheck);
            visualGroup.Controls.Add(restoreVisualBtn);

            // --- Startup Manager ---
            var startupGroup = new GroupBox
            {
                Text = Get("perf.startup_programs"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var startupList = new CheckedListBox
            {
                Location = new Point(15, 25),
                Size = new Size(400, 100),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                CheckOnClick = true
            };

            var startupDisableBtn = new Button
            {
                Text = Get("perf.disable_selected"),
                Location = new Point(15, 130),
                Size = new Size(130, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            startupDisableBtn.FlatAppearance.BorderSize = 0;

            var startupRefreshBtn = new Button
            {
                Text = Get("common.refresh"),
                Location = new Point(155, 130),
                Size = new Size(75, 28),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F)
            };

            startupGroup.Controls.Add(startupList);
            startupGroup.Controls.Add(startupDisableBtn);
            startupGroup.Controls.Add(startupRefreshBtn);

            LoadStartupPrograms(startupList);
            startupRefreshBtn.Click += (s, e) => LoadStartupPrograms(startupList);
            startupDisableBtn.Click += async (s, e) => await DisableSelectedStartupAsync(startupList);

            // --- One-Click Performance Boost ---
            var boostGroup = new GroupBox
            {
                Text = Get("perf.quick_actions"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var oneClickBtn = new Button
            {
                Text = Get("perf.one_click_boost"),
                Location = new Point(15, 28),
                Size = new Size(280, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 150, 80),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            oneClickBtn.FlatAppearance.BorderSize = 0;
            oneClickBtn.Click += async (s, e) => await OneClickPerformanceBoostAsync(disableTransparencyCheck.Checked);

            var boostInfoLabel = new Label
            {
                Text = Get("perf.boost_info"),
                Location = new Point(15, 72),
                Size = new Size(400, 20),
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.Gray
            };

            boostGroup.Controls.Add(oneClickBtn);
            boostGroup.Controls.Add(boostInfoLabel);

            // --- Game Mode Toggle ---
            var gameModeGroup = new GroupBox
            {
                Text = Get("perf.game_mode"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var gameModeStatus = new Label
            {
                Text = Get("common.checking"),
                Location = new Point(15, 28),
                Size = new Size(350, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var enableGameModeBtn = new Button
            {
                Text = Get("perf.enable_game_mode"),
                Location = new Point(15, 55),
                Size = new Size(150, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 150, 80),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            enableGameModeBtn.FlatAppearance.BorderSize = 0;
            enableGameModeBtn.Click += async (s, e) => await ToggleGameModeAsync(true, gameModeStatus);

            var disableGameModeBtn = new Button
            {
                Text = Get("perf.disable_game_mode"),
                Location = new Point(175, 55),
                Size = new Size(150, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            disableGameModeBtn.FlatAppearance.BorderSize = 0;
            disableGameModeBtn.Click += async (s, e) => await ToggleGameModeAsync(false, gameModeStatus);

            var gameModeNote = new Label
            {
                Text = Get("perf.game_mode_note"),
                Location = new Point(15, 95),
                Size = new Size(400, 30),
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.Gray
            };

            gameModeGroup.Controls.Add(gameModeStatus);
            gameModeGroup.Controls.Add(enableGameModeBtn);
            gameModeGroup.Controls.Add(disableGameModeBtn);
            gameModeGroup.Controls.Add(gameModeNote);

            CheckGameModeStatus(gameModeStatus);

            // --- Fast Startup Control ---
            var fastStartupGroup = new GroupBox
            {
                Text = Get("perf.fast_startup"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var fastStartupStatus = new Label
            {
                Text = Get("common.checking"),
                Location = new Point(15, 28),
                Size = new Size(350, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var enableFastStartupBtn = new Button
            {
                Text = Get("perf.enable_fast_startup"),
                Location = new Point(15, 55),
                Size = new Size(155, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 150, 80),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            enableFastStartupBtn.FlatAppearance.BorderSize = 0;
            enableFastStartupBtn.Click += async (s, e) => await ToggleFastStartupAsync(true, fastStartupStatus);

            var disableFastStartupBtn = new Button
            {
                Text = Get("perf.disable_fast_startup"),
                Location = new Point(180, 55),
                Size = new Size(155, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            disableFastStartupBtn.FlatAppearance.BorderSize = 0;
            disableFastStartupBtn.Click += async (s, e) => await ToggleFastStartupAsync(false, fastStartupStatus);

            var fastStartupNote = new Label
            {
                Text = Get("perf.fast_startup_note"),
                Location = new Point(15, 95),
                Size = new Size(400, 30),
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.DarkOrange
            };

            fastStartupGroup.Controls.Add(fastStartupStatus);
            fastStartupGroup.Controls.Add(enableFastStartupBtn);
            fastStartupGroup.Controls.Add(disableFastStartupBtn);
            fastStartupGroup.Controls.Add(fastStartupNote);

            CheckFastStartupStatus(fastStartupStatus);

            layout.Controls.Add(powerGroup, 0, 0);
            layout.Controls.Add(descPanel, 1, 0);
            layout.Controls.Add(visualGroup, 0, 1);
            layout.Controls.Add(startupGroup, 1, 1);
            layout.SetRowSpan(startupGroup, 2);
            layout.Controls.Add(boostGroup, 0, 2);
            layout.Controls.Add(gameModeGroup, 0, 3);
            layout.Controls.Add(fastStartupGroup, 1, 3);

            tab.Controls.Add(layout);
            return tab;
        }

        #endregion

        #region Privacy Tab

        private TabPage BuildPrivacyTab()
        {
            var tab = new TabPage(Get("tab.privacy"));
            tab.Padding = new Padding(10);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));

            // --- Telemetry ---
            var telemetryGroup = new GroupBox
            {
                Text = Get("priv.telemetry"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var telemetryStatus = new Label
            {
                Text = Get("common.checking"),
                Location = new Point(15, 28),
                Size = new Size(300, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var reduceTelemetryBtn = new Button
            {
                Text = Get("priv.reduce_telemetry"),
                Location = new Point(15, 55),
                Size = new Size(200, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            reduceTelemetryBtn.FlatAppearance.BorderSize = 0;
            reduceTelemetryBtn.Click += async (s, e) => await ReduceTelemetryAsync(telemetryStatus);

            var telemetryNote = new Label
            {
                Text = Get("priv.telemetry_note"),
                Location = new Point(15, 95),
                Size = new Size(400, 20),
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.DarkOrange
            };

            telemetryGroup.Controls.Add(telemetryStatus);
            telemetryGroup.Controls.Add(reduceTelemetryBtn);
            telemetryGroup.Controls.Add(telemetryNote);

            CheckTelemetryStatus(telemetryStatus);

            // --- Advertising ID ---
            var adGroup = new GroupBox
            {
                Text = Get("priv.ad_id"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var adStatus = new Label
            {
                Text = Get("common.checking"),
                Location = new Point(15, 28),
                Size = new Size(380, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var adCurrentIdLabel = new Label
            {
                Text = "",
                Location = new Point(15, 50),
                Size = new Size(380, 20),
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = Color.Gray
            };

            var disableAdIdBtn = new Button
            {
                Text = Get("priv.disable_ad_id"),
                Location = new Point(15, 75),
                Size = new Size(180, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            disableAdIdBtn.FlatAppearance.BorderSize = 0;
            disableAdIdBtn.Click += async (s, e) => await DisableAdvertisingIdAsync(adStatus);

            var resetAdIdBtn = new Button
            {
                Text = Get("priv.reset_ad_id"),
                Location = new Point(205, 75),
                Size = new Size(180, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            resetAdIdBtn.FlatAppearance.BorderSize = 0;
            resetAdIdBtn.Click += async (s, e) => await ResetAdvertisingIdAsync(adStatus, adCurrentIdLabel);

            adGroup.Controls.Add(adStatus);
            adGroup.Controls.Add(adCurrentIdLabel);
            adGroup.Controls.Add(disableAdIdBtn);
            adGroup.Controls.Add(resetAdIdBtn);

            CheckAdvertisingIdStatus(adStatus);
            LoadCurrentAdvertisingId(adCurrentIdLabel);

            // --- Activity History ---
            var activityGroup = new GroupBox
            {
                Text = Get("priv.activity_history"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var clearActivityBtn = new Button
            {
                Text = Get("priv.clear_activity"),
                Location = new Point(15, 28),
                Size = new Size(200, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            clearActivityBtn.FlatAppearance.BorderSize = 0;
            clearActivityBtn.Click += async (s, e) => await ClearActivityHistoryAsync();

            var activityNote = new Label
            {
                Text = Get("priv.activity_note"),
                Location = new Point(15, 68),
                Size = new Size(400, 40),
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.DarkOrange
            };

            activityGroup.Controls.Add(clearActivityBtn);
            activityGroup.Controls.Add(activityNote);

            // --- Recall / AI (24H2+) ---
            var recallGroup = new GroupBox
            {
                Text = Get("priv.recall"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Enabled = WindowsVersionChecker.Is24H2OrLater()
            };

            var recallStatus = new Label
            {
                Text = WindowsVersionChecker.Is24H2OrLater()
                    ? Get("priv.recall_checking")
                    : Get("priv.recall_unavailable"),
                Location = new Point(15, 28),
                Size = new Size(350, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var disableRecallBtn = new Button
            {
                Text = Get("priv.disable_recall"),
                Location = new Point(15, 55),
                Size = new Size(200, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F),
                Enabled = WindowsVersionChecker.Is24H2OrLater()
            };
            disableRecallBtn.FlatAppearance.BorderSize = 0;
            disableRecallBtn.Click += async (s, e) => await DisableRecallAsync(recallStatus);

            recallGroup.Controls.Add(recallStatus);
            recallGroup.Controls.Add(disableRecallBtn);

            if (WindowsVersionChecker.Is24H2OrLater())
                CheckRecallStatus(recallStatus);

            // --- Windows Security Link ---
            var securityGroup = new GroupBox
            {
                Text = Get("priv.security"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var openSecurityBtn = new Button
            {
                Text = Get("priv.open_security"),
                Location = new Point(15, 28),
                Size = new Size(200, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            openSecurityBtn.FlatAppearance.BorderSize = 0;
            openSecurityBtn.Click += (s, e) => _systemTools.OpenWindowsSettings("ms-settings:windowsdefender");

            var securityNote = new Label
            {
                Text = Get("priv.security_note"),
                Location = new Point(15, 68),
                Size = new Size(400, 20),
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.Gray
            };

            securityGroup.Controls.Add(openSecurityBtn);
            securityGroup.Controls.Add(securityNote);

            // --- DNS Changer ---
            var dnsGroup = new GroupBox
            {
                Text = Get("priv.dns"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var dnsStatus = new Label
            {
                Text = Get("common.checking"),
                Location = new Point(15, 28),
                Size = new Size(380, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var dnsCombo = new ComboBox
            {
                Location = new Point(15, 55),
                Size = new Size(220, 28),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9F)
            };
            dnsCombo.Items.AddRange(new object[]
            {
                Get("priv.dns_auto"),
                "Cloudflare (1.1.1.1)",
                "Google (8.8.8.8)",
                "Quad9 (9.9.9.9)",
                "OpenDNS (208.67.222.222)"
            });
            dnsCombo.SelectedIndex = 0;

            var applyDnsBtn = new Button
            {
                Text = Get("common.apply"),
                Location = new Point(245, 54),
                Size = new Size(100, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            applyDnsBtn.FlatAppearance.BorderSize = 0;
            applyDnsBtn.Click += async (s, e) => await SetDnsAsync(dnsCombo.SelectedIndex, dnsStatus);

            var dnsNote = new Label
            {
                Text = Get("priv.dns_note"),
                Location = new Point(15, 90),
                Size = new Size(380, 20),
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.Gray
            };

            dnsGroup.Controls.Add(dnsStatus);
            dnsGroup.Controls.Add(dnsCombo);
            dnsGroup.Controls.Add(applyDnsBtn);
            dnsGroup.Controls.Add(dnsNote);
            CheckDnsStatus(dnsStatus);

            // --- Hosts Ad Blocker ---
            var hostsGroup = new GroupBox
            {
                Text = Get("priv.hosts_blocker"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var hostsStatus = new Label
            {
                Text = Get("common.checking"),
                Location = new Point(15, 28),
                Size = new Size(380, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var enableHostsBtn = new Button
            {
                Text = Get("priv.hosts_enable"),
                Location = new Point(15, 55),
                Size = new Size(170, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            enableHostsBtn.FlatAppearance.BorderSize = 0;
            enableHostsBtn.Click += async (s, e) => await EnableHostsBlockerAsync(hostsStatus);

            var disableHostsBtn = new Button
            {
                Text = Get("priv.hosts_disable"),
                Location = new Point(195, 55),
                Size = new Size(170, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            disableHostsBtn.FlatAppearance.BorderSize = 0;
            disableHostsBtn.Click += async (s, e) => await DisableHostsBlockerAsync(hostsStatus);

            var hostsNote = new Label
            {
                Text = Get("priv.hosts_note"),
                Location = new Point(15, 95),
                Size = new Size(380, 20),
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.Gray
            };

            hostsGroup.Controls.Add(hostsStatus);
            hostsGroup.Controls.Add(enableHostsBtn);
            hostsGroup.Controls.Add(disableHostsBtn);
            hostsGroup.Controls.Add(hostsNote);
            CheckHostsBlockerStatus(hostsStatus);

            layout.RowCount = 5;
            layout.Controls.Add(telemetryGroup, 0, 0);
            layout.Controls.Add(adGroup, 1, 0);
            layout.Controls.Add(activityGroup, 0, 1);
            layout.Controls.Add(recallGroup, 1, 1);
            layout.Controls.Add(securityGroup, 0, 2);
            layout.Controls.Add(dnsGroup, 1, 2);
            layout.Controls.Add(hostsGroup, 0, 3);

            tab.Controls.Add(layout);
            return tab;
        }

        #endregion

        #region Cleanup Tab

        private TabPage BuildCleanupTab()
        {
            var tab = new TabPage(Get("tab.cleanup"));
            tab.Padding = new Padding(10);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            // --- Temp Files ---
            var tempGroup = new GroupBox
            {
                Text = Get("clean.temp_files"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var tempPreviewLabel = new Label
            {
                Text = Get("common.calculating"),
                Location = new Point(15, 28),
                Size = new Size(350, 35),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var cleanTempBtn = new Button
            {
                Text = Get("clean.clean_temp"),
                Location = new Point(15, 70),
                Size = new Size(150, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            cleanTempBtn.FlatAppearance.BorderSize = 0;
            cleanTempBtn.Click += async (s, e) => await CleanTempFilesAsync(tempPreviewLabel);

            tempGroup.Controls.Add(tempPreviewLabel);
            tempGroup.Controls.Add(cleanTempBtn);

            PreviewTempFilesAsync(tempPreviewLabel);

            // --- Browser Cache ---
            var browserGroup = new GroupBox
            {
                Text = Get("clean.browser_cache"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var browserPreviewLabel = new Label
            {
                Text = Get("common.calculating"),
                Location = new Point(15, 28),
                Size = new Size(350, 35),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var cleanBrowserBtn = new Button
            {
                Text = Get("clean.clean_browser"),
                Location = new Point(15, 70),
                Size = new Size(165, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            cleanBrowserBtn.FlatAppearance.BorderSize = 0;
            cleanBrowserBtn.Click += async (s, e) => await CleanBrowserCachesAsync(browserPreviewLabel);

            browserGroup.Controls.Add(browserPreviewLabel);
            browserGroup.Controls.Add(cleanBrowserBtn);

            PreviewBrowserCacheAsync(browserPreviewLabel);

            // --- Store Cache ---
            var storeGroup = new GroupBox
            {
                Text = Get("clean.store_cache"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var resetStoreBtn = new Button
            {
                Text = Get("clean.reset_store"),
                Location = new Point(15, 28),
                Size = new Size(150, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            resetStoreBtn.FlatAppearance.BorderSize = 0;
            resetStoreBtn.Click += async (s, e) => await ResetStoreCacheAsync();

            var storeNote = new Label
            {
                Text = Get("clean.store_note"),
                Location = new Point(15, 68),
                Size = new Size(400, 20),
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.Gray
            };

            storeGroup.Controls.Add(resetStoreBtn);
            storeGroup.Controls.Add(storeNote);

            // --- Recycle Bin ---
            var recycleGroup = new GroupBox
            {
                Text = Get("clean.recycle_bin"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var recycleSizeLabel = new Label
            {
                Text = Get("clean.calculating_size"),
                Location = new Point(15, 28),
                Size = new Size(350, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };
            LoadRecycleBinSize(recycleSizeLabel);

            var emptyRecycleBtn = new Button
            {
                Text = Get("clean.empty_recycle"),
                Location = new Point(15, 55),
                Size = new Size(150, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            emptyRecycleBtn.FlatAppearance.BorderSize = 0;
            emptyRecycleBtn.Click += (s, e) => { EmptyRecycleBin(); LoadRecycleBinSize(recycleSizeLabel); };

            recycleGroup.Controls.Add(recycleSizeLabel);
            recycleGroup.Controls.Add(emptyRecycleBtn);

            // --- System File Checker ---
            var sfcGroup = new GroupBox
            {
                Text = Get("clean.sfc"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var sfcBtn = new Button
            {
                Text = Get("clean.run_sfc"),
                Location = new Point(15, 28),
                Size = new Size(150, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            sfcBtn.FlatAppearance.BorderSize = 0;
            sfcBtn.Click += async (s, e) => await RunSfcScanAsync();

            var sfcNote = new Label
            {
                Text = Get("clean.sfc_note"),
                Location = new Point(15, 68),
                Size = new Size(400, 35),
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.Gray
            };

            sfcGroup.Controls.Add(sfcBtn);
            sfcGroup.Controls.Add(sfcNote);

            // --- DISM Cleanup ---
            var dismGroup = new GroupBox
            {
                Text = Get("clean.dism"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var dismBtn = new Button
            {
                Text = Get("clean.run_dism"),
                Location = new Point(15, 28),
                Size = new Size(150, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            dismBtn.FlatAppearance.BorderSize = 0;
            dismBtn.Click += async (s, e) => await RunDismCleanupAsync();

            var dismNote = new Label
            {
                Text = Get("clean.dism_note"),
                Location = new Point(15, 68),
                Size = new Size(400, 20),
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.Gray
            };

            dismGroup.Controls.Add(dismBtn);
            dismGroup.Controls.Add(dismNote);

            layout.Controls.Add(tempGroup, 0, 0);
            layout.Controls.Add(browserGroup, 1, 0);
            layout.Controls.Add(storeGroup, 0, 1);
            layout.Controls.Add(recycleGroup, 1, 1);
            layout.Controls.Add(sfcGroup, 0, 2);
            layout.Controls.Add(dismGroup, 1, 2);

            tab.Controls.Add(layout);
            return tab;
        }

        #endregion

        #region Performance Operations

        private async void LoadPowerPlansAsync(ComboBox combo)
        {
            try
            {
                var result = await _systemTools.ListPowerPlansAsync();
                if (result.Success && result.Data != null)
                {
                    combo.Items.Clear();
                    foreach (var (guid, name, active) in result.Data)
                    {
                        var item = new PowerPlanItem { Guid = guid, Name = name, Active = active };
                        combo.Items.Add(item);
                        if (active) combo.SelectedItem = item;
                    }
                }
            }
            catch { }
        }

        private async Task CreateUltimatePerfPlanAsync(ComboBox powerCombo)
        {
            var changes = new List<string>
            {
                Get("perf.op.create_ultimate"),
                Get("perf.op.create_ultimate_appear")
            };

            if (!PreviewDialog.ShowPreview(this, Get("perf.op.create_ultimate_title"), changes))
                return;

            await ExecuteWithSafetyAsync(Get("perf.op.create_ultimate_title"), async () =>
            {
                var result = await _systemTools.CreateUltimatePerformancePlanAsync();
                if (result.Success)
                {
                    SetStatus(result.Data ?? Get("perf.op.ultimate_created"), false);
                    LoadPowerPlansAsync(powerCombo);
                }
                else
                {
                    SetStatus(Get("failed", result.ErrorMessage ?? ""), true);
                }
            });
        }

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

            await ExecuteWithSafetyAsync(Get("perf.op.change_power_plan"), async () =>
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

        private async Task ApplyVisualEffectsOptimizeAsync(
            bool disableTransparency = true, bool disableAnimations = true, bool disableShadows = false)
        {
            var changes = new List<string>
            {
                "Registry: HKCU\\...\\Explorer\\VisualEffects\\VisualFXSetting = 2 (Best Performance)",
            };
            if (disableTransparency)
                changes.Add("Registry: HKCU\\...\\Themes\\Personalize\\EnableTransparency = 0");
            if (disableAnimations)
                changes.Add("Registry: HKCU\\Control Panel\\Desktop\\WindowMetrics\\MinAnimate = 0");
            if (disableShadows)
                changes.Add("Registry: HKCU\\...\\Explorer\\Advanced\\ListviewShadow = 0");
            changes.Add(Get("perf.op.create_rp"));

            if (!PreviewDialog.ShowPreview(this, Get("perf.op.visual_effects_title"), changes))
                return;

            await ExecuteWithSafetyAsync(Get("perf.op.visual_effects_title"), async () =>
            {
                var backups = new List<string>();

                // Set VisualFXSetting to Best Performance
                var r1 = _registry.SetValueWithBackup(
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
                    "VisualFXSetting", 2, RegistryValueKind.DWord);
                if (r1.Success) backups.Add(r1.Data!);

                // Disable transparency
                if (disableTransparency)
                {
                    var r2 = _registry.SetValueWithBackup(
                        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                        "EnableTransparency", 0, RegistryValueKind.DWord);
                    if (r2.Success) backups.Add(r2.Data!);
                }

                // Disable window animations
                if (disableAnimations)
                {
                    var r3 = _registry.SetValueWithBackup(
                        @"HKCU\Control Panel\Desktop\WindowMetrics",
                        "MinAnimate", "0", RegistryValueKind.String);
                    if (r3.Success) backups.Add(r3.Data!);
                }

                // Disable drop shadows
                if (disableShadows)
                {
                    var r4 = _registry.SetValueWithBackup(
                        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                        "ListviewShadow", 0, RegistryValueKind.DWord);
                    if (r4.Success) backups.Add(r4.Data!);
                }

                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("perf.op.revert_visual"),
                    BackupFiles = backups
                });
                UpdateUndoButton();

                SetStatus(Get("perf.op.visual_optimized"), false);
                await Task.CompletedTask;
            });
        }

        private async Task RestoreDefaultVisualEffectsAsync()
        {
            var changes = new List<string>
            {
                "Registry: HKCU\\...\\VisualEffects\\VisualFXSetting = 0 (Let Windows choose)",
                "Registry: HKCU\\...\\Themes\\Personalize\\EnableTransparency = 1",
                "Registry: HKCU\\Control Panel\\Desktop\\WindowMetrics\\MinAnimate = 1",
                "Registry: HKCU\\...\\Explorer\\Advanced\\ListviewShadow = 1",
                "Create System Restore Point before changes"
            };

            if (!PreviewDialog.ShowPreview(this, Get("perf.op.restore_visual_title"), changes))
                return;

            await ExecuteWithSafetyAsync(Get("perf.op.restore_visual_title"), async () =>
            {
                var backups = new List<string>();

                var r1 = _registry.SetValueWithBackup(
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
                    "VisualFXSetting", 0, RegistryValueKind.DWord);
                if (r1.Success) backups.Add(r1.Data!);

                var r2 = _registry.SetValueWithBackup(
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "EnableTransparency", 1, RegistryValueKind.DWord);
                if (r2.Success) backups.Add(r2.Data!);

                var r3 = _registry.SetValueWithBackup(
                    @"HKCU\Control Panel\Desktop\WindowMetrics",
                    "MinAnimate", "1", RegistryValueKind.String);
                if (r3.Success) backups.Add(r3.Data!);

                var r4 = _registry.SetValueWithBackup(
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    "ListviewShadow", 1, RegistryValueKind.DWord);
                if (r4.Success) backups.Add(r4.Data!);

                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("perf.op.revert_to_optimized_visual"),
                    BackupFiles = backups
                });
                UpdateUndoButton();

                SetStatus(Get("perf.op.visual_restored"), false);
                await Task.CompletedTask;
            });
        }

        private void LoadStartupPrograms(CheckedListBox list)
        {
            list.Items.Clear();
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", false);
                if (key != null)
                {
                    foreach (var name in key.GetValueNames())
                    {
                        var value = key.GetValue(name)?.ToString() ?? "";
                        list.Items.Add(new StartupItem
                        {
                            Name = name,
                            Command = value,
                            Source = "Registry (HKCU)"
                        }, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("LoadStartupPrograms", "HKCU\\Run", ex);
            }
        }

        private void CheckGameModeStatus(Label label)
        {
            try
            {
                var result = _registry.GetValue(
                    @"HKCU\Software\Microsoft\GameBar",
                    "AllowAutoGameMode");
                var result2 = _registry.GetValue(
                    @"HKCU\Software\Microsoft\GameBar",
                    "AutoGameModeEnabled");

                // Both keys control Game Mode; AutoGameModeEnabled is primary in newer builds
                bool enabled = true; // default is enabled
                if (result2.Success && result2.Data != null)
                    enabled = Convert.ToInt32(result2.Data) == 1;
                else if (result.Success && result.Data != null)
                    enabled = Convert.ToInt32(result.Data) == 1;

                label.Text = enabled
                    ? Get("perf.gamemode_enabled")
                    : Get("perf.gamemode_disabled");
                label.ForeColor = enabled ? Color.Green : Color.DarkOrange;
            }
            catch
            {
                label.Text = Get("common.unable_determine");
                label.ForeColor = Color.Gray;
            }
        }

        private async Task ToggleGameModeAsync(bool enable, Label statusLabel)
        {
            int value = enable ? 1 : 0;
            string action = enable ? Get("common.enable") : Get("common.disable");

            var changes = new List<string>
            {
                $"Registry: HKCU\\Software\\Microsoft\\GameBar\\AllowAutoGameMode = {value}",
                $"Registry: HKCU\\Software\\Microsoft\\GameBar\\AutoGameModeEnabled = {value}",
                enable
                    ? Get("perf.op.gamemode_prioritizes")
                    : Get("perf.op.gamemode_restores")
            };

            if (!PreviewDialog.ShowPreview(this, $"{action} {Get("perf.game_mode")}", changes))
                return;

            await ExecuteWithSafetyAsync($"{action} {Get("perf.game_mode")}", async () =>
            {
                var backups = new List<string>();

                var r1 = _registry.SetValueWithBackup(
                    @"HKCU\Software\Microsoft\GameBar",
                    "AllowAutoGameMode", value, RegistryValueKind.DWord);
                if (r1.Success) backups.Add(r1.Data!);

                var r2 = _registry.SetValueWithBackup(
                    @"HKCU\Software\Microsoft\GameBar",
                    "AutoGameModeEnabled", value, RegistryValueKind.DWord);
                if (r2.Success) backups.Add(r2.Data!);

                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("perf.op.revert_gamemode", enable ? Get("word.disabled") : Get("word.enabled")),
                    BackupFiles = backups
                });
                UpdateUndoButton();

                CheckGameModeStatus(statusLabel);
                SetStatus(Get("perf.op.gamemode_set", enable ? Get("word.enabled") : Get("word.disabled")), false);
                await Task.CompletedTask;
            });
        }

        private void CheckFastStartupStatus(Label label)
        {
            try
            {
                var result = _registry.GetValue(
                    @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power",
                    "HiberbootEnabled");

                if (result.Success && result.Data != null)
                {
                    int val = Convert.ToInt32(result.Data);
                    label.Text = val == 1
                        ? Get("perf.faststartup_enabled")
                        : Get("perf.faststartup_disabled");
                    label.ForeColor = val == 1 ? Color.Green : Color.DarkOrange;
                }
                else
                {
                    label.Text = Get("perf.faststartup_default");
                    label.ForeColor = Color.Gray;
                }
            }
            catch
            {
                label.Text = Get("common.unable_determine");
                label.ForeColor = Color.Gray;
            }
        }

        private async Task ToggleFastStartupAsync(bool enable, Label statusLabel)
        {
            int value = enable ? 1 : 0;
            string action = enable ? Get("common.enable") : Get("common.disable");

            var changes = new List<string>
            {
                $"Registry: HKLM\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power\\HiberbootEnabled = {value}",
                enable
                    ? Get("perf.op.fast_startup_hibernate")
                    : Get("perf.op.fast_startup_disable_note"),
            };
            if (!enable)
                changes.Add(Get("perf.op.fast_startup_longer_boot"));

            if (!PreviewDialog.ShowPreview(this, $"{action} {Get("perf.fast_startup")}", changes))
                return;

            await ExecuteWithSafetyAsync($"{action} {Get("perf.fast_startup")}", async () =>
            {
                var backups = new List<string>();

                var r1 = _registry.SetValueWithBackup(
                    @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power",
                    "HiberbootEnabled", value, RegistryValueKind.DWord);
                if (r1.Success) backups.Add(r1.Data!);

                // Also toggle hibernation via powercfg for consistency
                if (!enable)
                {
                    await _systemTools.ExecuteToolAsync("powercfg.exe", "/hibernate off");
                }
                else
                {
                    await _systemTools.ExecuteToolAsync("powercfg.exe", "/hibernate on");
                }

                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("perf.op.revert_fast_startup", enable ? Get("word.disabled") : Get("word.enabled")),
                    BackupFiles = backups,
                    UndoDelegate = async () =>
                    {
                        int revert = enable ? 0 : 1;
                        _registry.SetValueWithBackup(
                            @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power",
                            "HiberbootEnabled", revert, RegistryValueKind.DWord);
                        await _systemTools.ExecuteToolAsync("powercfg.exe",
                            revert == 1 ? "/hibernate on" : "/hibernate off");
                        return Result<bool>.Ok(true);
                    }
                });
                UpdateUndoButton();

                CheckFastStartupStatus(statusLabel);
                SetStatus(Get("perf.op.fast_startup_set", enable ? Get("word.enabled") : Get("word.disabled")), false);
            });
        }

        private async Task DisableSelectedStartupAsync(CheckedListBox list)
        {
            var selected = new List<StartupItem>();
            for (int i = 0; i < list.Items.Count; i++)
            {
                if (list.GetItemChecked(i) && list.Items[i] is StartupItem item)
                    selected.Add(item);
            }

            if (selected.Count == 0)
            {
                MessageBox.Show(this, Get("perf.op.no_startup_selected"), Get("dialog.kwin"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var changes = selected.Select(s => $"Disable: {s.Name} ({s.Command})").ToList();
            changes.Insert(0, Get("perf.op.startup_will_disable"));

            if (!PreviewDialog.ShowPreview(this, Get("perf.op.disable_startup_title"), changes,
                Get("perf.op.startup_warning")))
                return;

            await ExecuteWithSafetyAsync(Get("perf.op.disable_startup_title"), async () =>
            {
                var backups = new List<string>();
                foreach (var item in selected)
                {
                    var result = _registry.DeleteValueWithBackup(
                        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
                        item.Name);
                    if (result.Success) backups.Add(result.Data!);
                }

                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("perf.op.reenable_startup", selected.Count),
                    BackupFiles = backups
                });
                UpdateUndoButton();

                SetStatus(Get("perf.op.disabled_startup", selected.Count), false);
                LoadStartupPrograms(list);
                await Task.CompletedTask;
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

            await ExecuteWithSafetyAsync(Get("perf.op.one_click_title"), async () =>
            {
                var backups = new List<string>();

                // 1. Power Plan
                var currentPlan = await _systemTools.GetActivePowerPlanAsync();
                string previousGuid = currentPlan.Success ? currentPlan.Data! : "";
                await _systemTools.SetPowerPlanAsync("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

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
                    UndoDelegate = async () =>
                    {
                        // Restore power plan
                        if (!string.IsNullOrEmpty(previousGuid))
                            await _systemTools.SetPowerPlanAsync(previousGuid);

                        // Restore registry from backups
                        foreach (var backup in backups)
                        {
                            if (File.Exists(backup))
                                await _registry.RestoreFromBackup(backup);
                        }
                        return Result<bool>.Ok(true);
                    }
                });
                UpdateUndoButton();

                SetStatus(Get("perf.op.boost_applied"), false);
            });
        }

        #endregion

        #region Privacy Operations

        private void CheckTelemetryStatus(Label label)
        {
            try
            {
                var result = _registry.GetValue(
                    @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                    "AllowTelemetry");
                if (result.Success && result.Data != null)
                {
                    int val = Convert.ToInt32(result.Data);
                    label.Text = val switch
                    {
                        0 => Get("priv.telemetry_security"),
                        1 => Get("priv.telemetry_basic"),
                        2 => Get("priv.telemetry_enhanced"),
                        3 => Get("priv.telemetry_full"),
                        _ => Get("priv.telemetry_unknown", val)
                    };
                    label.ForeColor = val <= 1 ? Color.Green : Color.DarkOrange;
                }
                else
                {
                    label.Text = Get("priv.telemetry_default");
                    label.ForeColor = Color.Gray;
                }
            }
            catch
            {
                label.Text = Get("common.unable_determine");
            }
        }

        private async Task ReduceTelemetryAsync(Label statusLabel)
        {
            var changes = new List<string>
            {
                "Registry: HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection\\AllowTelemetry = 1 (Basic)",
                "Service: Stop and disable DiagTrack (Connected User Experiences and Telemetry)",
                "Create System Restore Point before changes"
            };

            if (!PreviewDialog.ShowPreview(this, Get("priv.op.reduce_telemetry_title"), changes,
                Get("priv.op.telemetry_may_affect")))
                return;

            await ExecuteWithSafetyAsync(Get("priv.op.reduce_telemetry_title"), async () =>
            {
                var backups = new List<string>();

                // Set telemetry to Basic (1)
                var r1 = _registry.SetValueWithBackup(
                    @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                    "AllowTelemetry", 1, RegistryValueKind.DWord);
                if (r1.Success) backups.Add(r1.Data!);

                // Stop DiagTrack service
                try
                {
                    await _systemTools.StopAndDisableServiceAsync("DiagTrack");
                }
                catch (Exception ex)
                {
                    Logger.Instance.Warn("ReduceTelemetry", "DiagTrack", ex.Message);
                }

                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("priv.op.revert_telemetry"),
                    BackupFiles = backups
                });
                UpdateUndoButton();

                CheckTelemetryStatus(statusLabel);
                SetStatus(Get("priv.op.telemetry_reduced"), false);
            });
        }

        private void CheckAdvertisingIdStatus(Label label)
        {
            try
            {
                var result = _registry.GetValue(
                    @"HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo",
                    "DisabledByGroupPolicy");
                if (result.Success && result.Data is int val && val == 1)
                {
                    label.Text = Get("priv.ad_disabled");
                    label.ForeColor = Color.Green;
                }
                else
                {
                    label.Text = Get("priv.ad_enabled");
                    label.ForeColor = Color.DarkOrange;
                }
            }
            catch
            {
                label.Text = Get("common.unable_determine");
            }
        }

        private void LoadCurrentAdvertisingId(Label label)
        {
            try
            {
                var result = _registry.GetValue(
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
                    "Id");
                if (result.Success && result.Data != null)
                {
                    string id = result.Data.ToString() ?? "";
                    label.Text = Get("priv.ad_current", id.Length > 36 ? id[..36] + "..." : id);
                }
                else
                {
                    label.Text = Get("priv.ad_not_set");
                }
            }
            catch
            {
                label.Text = Get("priv.ad_unable_read");
            }
        }

        private async Task DisableAdvertisingIdAsync(Label statusLabel)
        {
            var changes = new List<string>
            {
                "Registry: HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\AdvertisingInfo\\DisabledByGroupPolicy = 1",
                "This prevents apps from using your advertising ID for personalized ads."
            };

            if (!PreviewDialog.ShowPreview(this, Get("priv.op.disable_ad_title"), changes))
                return;

            await ExecuteWithSafetyAsync(Get("priv.op.disable_ad_title"), async () =>
            {
                var backups = new List<string>();

                var r1 = _registry.SetValueWithBackup(
                    @"HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo",
                    "DisabledByGroupPolicy", 1, RegistryValueKind.DWord);
                if (r1.Success) backups.Add(r1.Data!);

                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("priv.op.reenable_ad"),
                    BackupFiles = backups
                });
                UpdateUndoButton();

                CheckAdvertisingIdStatus(statusLabel);
                SetStatus(Get("priv.op.ad_disabled"), false);
                await Task.CompletedTask;
            });
        }

        private async Task ResetAdvertisingIdAsync(Label statusLabel, Label idLabel)
        {
            var changes = new List<string>
            {
                "Registry: HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo\\Id = (new random GUID)",
                "This generates a new random Advertising ID, breaking ad tracking linkage.",
                "Your old Advertising ID will be backed up and can be restored via Undo."
            };

            if (!PreviewDialog.ShowPreview(this, Get("priv.op.reset_ad_title"), changes,
                Get("priv.op.ad_replaces_warning")))
                return;

            await ExecuteWithSafetyAsync(Get("priv.op.reset_ad_title"), async () =>
            {
                var backups = new List<string>();

                string newId = Guid.NewGuid().ToString();

                var r1 = _registry.SetValueWithBackup(
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
                    "Id", newId, RegistryValueKind.String);
                if (r1.Success) backups.Add(r1.Data!);

                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("priv.op.revert_ad_reset"),
                    BackupFiles = backups
                });
                UpdateUndoButton();

                CheckAdvertisingIdStatus(statusLabel);
                LoadCurrentAdvertisingId(idLabel);
                SetStatus(Get("priv.op.ad_reset_done"), false);
                await Task.CompletedTask;
            });
        }

        private async Task ClearActivityHistoryAsync()
        {
            var confirm = MessageBox.Show(this,
                Get("priv.op.clear_activity_confirm"),
                Get("priv.op.confirm_irreversible"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes) return;

            await ExecuteWithSafetyAsync("Clear Activity History", async () =>
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

                // 2. Delete Connected Devices Platform data
                string cdpPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ConnectedDevicesPlatform");

                if (Directory.Exists(cdpPath))
                {
                    foreach (var file in Directory.EnumerateFiles(cdpPath, "*", SearchOption.AllDirectories))
                    {
                        try { File.Delete(file); filesDeleted++; } catch { }
                    }
                }

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

        private void CheckRecallStatus(Label label)
        {
            try
            {
                var result = _registry.GetValue(
                    @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI",
                    "DisableAIDataAnalysis");
                if (result.Success && result.Data is int val && val == 1)
                {
                    label.Text = Get("priv.recall_disabled");
                    label.ForeColor = Color.Green;
                }
                else
                {
                    label.Text = Get("priv.recall_active");
                    label.ForeColor = Color.DarkOrange;
                }
            }
            catch
            {
                label.Text = Get("common.unable_determine");
            }
        }

        private async Task DisableRecallAsync(Label statusLabel)
        {
            var changes = new List<string>
            {
                "Registry: HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsAI\\DisableAIDataAnalysis = 1",
                "This disables Windows Recall / AI snapshot features on Windows 11 24H2+."
            };

            if (!PreviewDialog.ShowPreview(this, Get("priv.op.disable_recall_title"), changes))
                return;

            await ExecuteWithSafetyAsync(Get("priv.op.disable_recall_title"), async () =>
            {
                var backups = new List<string>();

                var r1 = _registry.SetValueWithBackup(
                    @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI",
                    "DisableAIDataAnalysis", 1, RegistryValueKind.DWord);
                if (r1.Success) backups.Add(r1.Data!);

                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("priv.op.reenable_recall"),
                    BackupFiles = backups
                });
                UpdateUndoButton();

                CheckRecallStatus(statusLabel);
                SetStatus(Get("priv.op.recall_disabled"), false);
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

        private static readonly string[] DnsNames = { "DHCP (Auto)", "Cloudflare", "Google", "Quad9", "OpenDNS" };

        private void CheckDnsStatus(Label label)
        {
            try
            {
                var ni = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                        && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback);
                if (ni == null) { label.Text = Get("priv.dns_no_adapter"); return; }
                var dns = ni.GetIPProperties().DnsAddresses;
                if (dns.Count == 0) { label.Text = Get("priv.dns_status_auto"); return; }
                string primary = dns[0].ToString();
                // Match known providers
                for (int i = 1; i < DnsServers.Length; i++)
                {
                    if (DnsServers[i][0] == primary)
                    {
                        label.Text = Get("priv.dns_status_set", DnsNames[i], primary);
                        return;
                    }
                }
                label.Text = Get("priv.dns_status_custom", primary);
            }
            catch { label.Text = Get("common.unable_determine"); }
        }

        private async Task SetDnsAsync(int index, Label statusLabel)
        {
            if (index == 0)
            {
                // Reset to DHCP
                SetBusy(true, Get("priv.dns_resetting"));
                try
                {
                    var iface = GetActiveInterfaceName();
                    if (iface == null) { SetStatus(Get("priv.dns_no_adapter"), false); return; }

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"interface ip set dns name=\"{iface}\" dhcp",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true
                    };
                    var proc = System.Diagnostics.Process.Start(psi);
                    await proc!.WaitForExitAsync();
                    CheckDnsStatus(statusLabel);
                    SetStatus(Get("priv.dns_set_auto"), false);
                }
                catch (Exception ex) { SetStatus(Get("failed", ex.Message), false); }
            }
            else
            {
                var servers = DnsServers[index];
                var name = DnsNames[index];
                var changes = new List<string>
                {
                    Get("priv.op.dns_set_desc", name, servers[0], servers[1])
                };

                if (!PreviewDialog.ShowPreview(this, Get("priv.op.dns_title"), changes))
                    return;

                SetBusy(true, Get("priv.dns_applying", name));
                try
                {
                    var iface = GetActiveInterfaceName();
                    if (iface == null) { SetStatus(Get("priv.dns_no_adapter"), false); return; }

                    // Set primary DNS
                    var psi1 = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"interface ip set dns name=\"{iface}\" static {servers[0]}",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true
                    };
                    var proc1 = System.Diagnostics.Process.Start(psi1);
                    await proc1!.WaitForExitAsync();

                    // Set secondary DNS
                    var psi2 = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"interface ip add dns name=\"{iface}\" {servers[1]} index=2",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true
                    };
                    var proc2 = System.Diagnostics.Process.Start(psi2);
                    await proc2!.WaitForExitAsync();

                    CheckDnsStatus(statusLabel);
                    SetStatus(Get("priv.dns_set_ok", name), false);
                }
                catch (Exception ex) { SetStatus(Get("failed", ex.Message), false); }
            }
            SetBusy(false, null);
        }

        private static string? GetActiveInterfaceName()
        {
            return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                    && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                ?.Name;
        }

        // --- Hosts-based Ad Blocker ---
        private static readonly string HostsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "drivers", "etc", "hosts");

        private const string HostsMarkerStart = "# >>> K-win Ad Blocker Start >>>";
        private const string HostsMarkerEnd = "# <<< K-win Ad Blocker End <<<";

        private void CheckHostsBlockerStatus(Label label)
        {
            try
            {
                if (File.Exists(HostsPath))
                {
                    var content = File.ReadAllText(HostsPath);
                    if (content.Contains(HostsMarkerStart))
                    {
                        // Count blocked domains
                        int count = 0;
                        bool inBlock = false;
                        foreach (var line in content.Split('\n'))
                        {
                            if (line.TrimStart().StartsWith(HostsMarkerStart)) { inBlock = true; continue; }
                            if (line.TrimStart().StartsWith(HostsMarkerEnd)) { inBlock = false; continue; }
                            if (inBlock && line.StartsWith("0.0.0.0")) count++;
                        }
                        label.Text = Get("priv.hosts_active", count);
                        return;
                    }
                }
                label.Text = Get("priv.hosts_inactive");
            }
            catch { label.Text = Get("common.unable_determine"); }
        }

        private async Task EnableHostsBlockerAsync(Label statusLabel)
        {
            var changes = new List<string>
            {
                Get("priv.op.hosts_add_desc"),
                Get("priv.op.hosts_domains")
            };

            if (!PreviewDialog.ShowPreview(this, Get("priv.op.hosts_title"), changes))
                return;

            SetBusy(true, Get("priv.hosts_downloading"));
            try
            {
                // Backup existing hosts file
                string backupPath = HostsPath + ".kwin.bak";
                if (File.Exists(HostsPath) && !File.Exists(backupPath))
                    File.Copy(HostsPath, backupPath, false);

                // Download Steven Black's unified hosts list (ads only)
                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromSeconds(30);
                var hostsData = await http.GetStringAsync(
                    "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts");

                // Parse — extract 0.0.0.0 lines (skip localhost entries)
                var lines = hostsData.Split('\n')
                    .Where(l => l.StartsWith("0.0.0.0") && !l.Contains("0.0.0.0 0.0.0.0") && !l.Contains("localhost"))
                    .Take(15000) // reasonable limit
                    .ToList();

                // Read existing hosts, remove old K-win block if any
                string existingContent = File.Exists(HostsPath) ? File.ReadAllText(HostsPath) : "";
                existingContent = RemoveKwinHostsBlock(existingContent);

                // Append new block
                var sb = new System.Text.StringBuilder(existingContent.TrimEnd());
                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine(HostsMarkerStart);
                sb.AppendLine($"# Added by K-win on {DateTime.Now:yyyy-MM-dd HH:mm}");
                sb.AppendLine($"# Source: Steven Black's unified hosts — {lines.Count} domains blocked");
                foreach (var line in lines) sb.AppendLine(line.Trim());
                sb.AppendLine(HostsMarkerEnd);

                File.WriteAllText(HostsPath, sb.ToString());

                // Flush DNS cache
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ipconfig",
                    Arguments = "/flushdns",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                System.Diagnostics.Process.Start(psi)?.WaitForExit(5000);

                CheckHostsBlockerStatus(statusLabel);
                SetStatus(Get("priv.hosts_enabled_ok", lines.Count), false);
            }
            catch (Exception ex) { SetStatus(Get("failed", ex.Message), false); }
            SetBusy(false, null);
        }

        private async Task DisableHostsBlockerAsync(Label statusLabel)
        {
            SetBusy(true, Get("priv.hosts_removing"));
            try
            {
                if (File.Exists(HostsPath))
                {
                    var content = File.ReadAllText(HostsPath);
                    content = RemoveKwinHostsBlock(content);
                    File.WriteAllText(HostsPath, content.TrimEnd() + Environment.NewLine);

                    // Flush DNS
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "ipconfig",
                        Arguments = "/flushdns",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    System.Diagnostics.Process.Start(psi)?.WaitForExit(5000);
                }
                CheckHostsBlockerStatus(statusLabel);
                SetStatus(Get("priv.hosts_disabled_ok"), false);
            }
            catch (Exception ex) { SetStatus(Get("failed", ex.Message), false); }
            SetBusy(false, null);
            await Task.CompletedTask;
        }

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

        private async void PreviewTempFilesAsync(Label label)
        {
            await Task.Run(() =>
            {
                var result = _cleanup.PreviewTempFileCleanup();
                if (result.Success)
                {
                    var (total, breakdown) = result.Data;
                    string text = $"Estimated: {CleanupManager.FormatSize(total)} can be freed\n" +
                                  string.Join(", ", breakdown.Select(b =>
                                      $"{b.Key}: {CleanupManager.FormatSize(b.Value)}"));
                    BeginInvoke(() => label.Text = text);
                }
                else
                {
                    BeginInvoke(() => label.Text = Get("clean.unable_calculate"));
                }
            });
        }

        private async void PreviewBrowserCacheAsync(Label label)
        {
            await Task.Run(() =>
            {
                var result = _cleanup.PreviewBrowserCacheCleanup();
                if (result.Success)
                {
                    var (total, breakdown) = result.Data;
                    if (total == 0)
                    {
                        BeginInvoke(() => label.Text = Get("clean.no_browser_cache"));
                        return;
                    }
                    string text = $"Estimated: {CleanupManager.FormatSize(total)} can be freed\n" +
                                  string.Join(", ", breakdown.Select(b =>
                                      $"{b.Key}: {CleanupManager.FormatSize(b.Value)}"));
                    BeginInvoke(() => label.Text = text);
                }
                else
                {
                    BeginInvoke(() => label.Text = Get("clean.unable_calculate"));
                }
            });
        }

        private async Task CleanTempFilesAsync(Label previewLabel)
        {
            var preview = _cleanup.PreviewTempFileCleanup();
            if (preview.Success)
            {
                var changes = preview.Data.Breakdown
                    .Select(b => $"{b.Key}: {CleanupManager.FormatSize(b.Value)}")
                    .ToList();
                changes.Insert(0, $"Total estimated: {CleanupManager.FormatSize(preview.Data.TotalBytes)}");

                if (!PreviewDialog.ShowPreview(this, Get("clean.op.clean_temp_title"), changes))
                    return;
            }

            SetBusy(true, Get("clean.op.cleaning_temp"));
            try
            {
                var progress = new Progress<string>(msg => SetStatus(msg, false));
                var result = await Task.Run(() => _cleanup.CleanTempFilesAsync(progress));

                if (result.Success)
                {
                    var (files, bytes) = result.Data;
                    SetStatus(Get("clean.op.cleaned_files", files, CleanupManager.FormatSize(bytes)), false);
                }
                else
                {
                    SetStatus(Get("clean.op.cleanup_failed", result.ErrorMessage ?? ""), true);
                }

                PreviewTempFilesAsync(previewLabel);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task CleanBrowserCachesAsync(Label previewLabel)
        {
            var preview = _cleanup.PreviewBrowserCacheCleanup();
            if (preview.Success)
            {
                var changes = preview.Data.Breakdown
                    .Select(b => $"{b.Key}: {CleanupManager.FormatSize(b.Value)}")
                    .ToList();
                changes.Insert(0, $"Total estimated: {CleanupManager.FormatSize(preview.Data.TotalBytes)}");

                bool anyRunning = SystemTools.IsProcessRunning("msedge") ||
                                  SystemTools.IsProcessRunning("chrome") ||
                                  SystemTools.IsProcessRunning("firefox");

                if (!PreviewDialog.ShowPreview(this, Get("clean.op.clean_browser_title"), changes,
                    anyRunning ? Get("clean.op.browser_running_warning") : null))
                    return;
            }

            SetBusy(true, Get("clean.op.cleaning_browser"));
            try
            {
                var progress = new Progress<string>(msg => SetStatus(msg, false));
                var result = await Task.Run(() => _cleanup.CleanBrowserCachesAsync(progress));

                if (result.Success)
                {
                    var (files, bytes, warnings) = result.Data;
                    SetStatus(Get("clean.op.cleaned_files", files, CleanupManager.FormatSize(bytes)), false);

                    if (warnings.Count > 0)
                    {
                        MessageBox.Show(this, string.Join("\n", warnings),
                            Get("dialog.kwin_warnings"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                else
                {
                    SetStatus(Get("clean.op.cleanup_failed", result.ErrorMessage ?? ""), true);
                }

                PreviewBrowserCacheAsync(previewLabel);
            }
            finally
            {
                SetBusy(false);
            }
        }

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

        private void EmptyRecycleBin()
        {
            var confirm = MessageBox.Show(this,
                Get("clean.op.recycle_confirm"),
                Get("dialog.kwin_confirm"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            var result = _cleanup.EmptyRecycleBin();
            SetStatus(result.Success
                ? Get("clean.op.recycle_emptied")
                : Get("failed", result.ErrorMessage ?? ""), !result.Success);
        }

        private void LoadRecycleBinSize(Label label)
        {
            var result = CleanupManager.GetRecycleBinSize();
            if (result.Success)
            {
                var (size, count) = result.Data;
                label.Text = count > 0
                    ? Get("clean.recycle_items", count, CleanupManager.FormatSize(size))
                    : Get("clean.recycle_empty");
                label.ForeColor = count > 0 ? Color.DarkOrange : Color.Green;
            }
            else
            {
                label.Text = Get("clean.recycle_unable");
                label.ForeColor = Color.Gray;
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

            SetBusy(true, Get("clean.op.running_sfc"));
            _progressBar.Style = ProgressBarStyle.Marquee;
            try
            {
                var progress = new Progress<string>(msg => SetStatus(msg, false));
                var result = await _cleanup.RunSystemFileCheckAsync(progress);

                if (result.Success)
                {
                    SetStatus(Get("clean.op.sfc_complete"), false);
                    MessageBox.Show(this, result.Data ?? "Scan complete.",
                        Get("clean.op.sfc_results"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    SetStatus(Get("failed", result.ErrorMessage ?? ""), true);
                }
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
            }
            finally
            {
                _progressBar.Style = ProgressBarStyle.Continuous;
                SetBusy(false);
            }
        }

        #endregion

        #region Tweaks Tab

        private TabPage BuildTweaksTab()
        {
            var tab = new TabPage(Get("tab.tweaks"));
            tab.Padding = new Padding(10);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                AutoScroll = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            // --- Dark / Light Mode ---
            var darkModeGroup = new GroupBox
            {
                Text = Get("tweak.dark_mode"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var darkModeStatus = new Label
            {
                Text = Get("common.checking"),
                Location = new Point(15, 28),
                Size = new Size(350, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var enableDarkBtn = new Button
            {
                Text = Get("tweak.enable_dark"),
                Location = new Point(15, 55),
                Size = new Size(160, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            enableDarkBtn.FlatAppearance.BorderSize = 0;
            enableDarkBtn.Click += async (s, e) => await ToggleDarkModeAsync(true, darkModeStatus);

            var enableLightBtn = new Button
            {
                Text = Get("tweak.enable_light"),
                Location = new Point(185, 55),
                Size = new Size(160, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(240, 240, 240),
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", 9F)
            };
            enableLightBtn.FlatAppearance.BorderSize = 0;
            enableLightBtn.Click += async (s, e) => await ToggleDarkModeAsync(false, darkModeStatus);

            darkModeGroup.Controls.Add(darkModeStatus);
            darkModeGroup.Controls.Add(enableDarkBtn);
            darkModeGroup.Controls.Add(enableLightBtn);
            CheckDarkModeStatus(darkModeStatus);

            // --- Transparency Toggle ---
            var transparencyGroup = new GroupBox
            {
                Text = Get("tweak.transparency"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var transparencyStatus = new Label
            {
                Text = Get("common.checking"),
                Location = new Point(15, 28),
                Size = new Size(350, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var enableTransBtn = new Button
            {
                Text = Get("tweak.enable_transparency"),
                Location = new Point(15, 55),
                Size = new Size(160, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 150, 80),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            enableTransBtn.FlatAppearance.BorderSize = 0;
            enableTransBtn.Click += async (s, e) => await ToggleTransparencyAsync(true, transparencyStatus);

            var disableTransBtn = new Button
            {
                Text = Get("tweak.disable_transparency"),
                Location = new Point(185, 55),
                Size = new Size(160, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            disableTransBtn.FlatAppearance.BorderSize = 0;
            disableTransBtn.Click += async (s, e) => await ToggleTransparencyAsync(false, transparencyStatus);

            transparencyGroup.Controls.Add(transparencyStatus);
            transparencyGroup.Controls.Add(enableTransBtn);
            transparencyGroup.Controls.Add(disableTransBtn);
            CheckTransparencyStatus(transparencyStatus);

            // --- Show File Extensions ---
            var fileExtGroup = new GroupBox
            {
                Text = Get("tweak.file_ext"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var fileExtStatus = new Label
            {
                Text = Get("common.checking"),
                Location = new Point(15, 28),
                Size = new Size(350, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var showExtBtn = new Button
            {
                Text = Get("tweak.show_ext"),
                Location = new Point(15, 55),
                Size = new Size(160, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            showExtBtn.FlatAppearance.BorderSize = 0;
            showExtBtn.Click += async (s, e) => await ToggleFileExtensionsAsync(true, fileExtStatus);

            var hideExtBtn = new Button
            {
                Text = Get("tweak.hide_ext"),
                Location = new Point(185, 55),
                Size = new Size(160, 32),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F)
            };
            hideExtBtn.Click += async (s, e) => await ToggleFileExtensionsAsync(false, fileExtStatus);

            var fileExtNote = new Label
            {
                Text = Get("tweak.ext_note"),
                Location = new Point(15, 95),
                Size = new Size(380, 20),
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.Gray
            };

            fileExtGroup.Controls.Add(fileExtStatus);
            fileExtGroup.Controls.Add(showExtBtn);
            fileExtGroup.Controls.Add(hideExtBtn);
            fileExtGroup.Controls.Add(fileExtNote);
            CheckFileExtensionsStatus(fileExtStatus);

            // --- Widgets Disable ---
            var widgetsGroup = new GroupBox
            {
                Text = Get("tweak.widgets"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var widgetsStatus = new Label
            {
                Text = Get("common.checking"),
                Location = new Point(15, 28),
                Size = new Size(350, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var disableWidgetsBtn = new Button
            {
                Text = Get("tweak.disable_widgets"),
                Location = new Point(15, 55),
                Size = new Size(145, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            disableWidgetsBtn.FlatAppearance.BorderSize = 0;
            disableWidgetsBtn.Click += async (s, e) => await ToggleWidgetsAsync(false, widgetsStatus);

            var enableWidgetsBtn = new Button
            {
                Text = Get("tweak.enable_widgets"),
                Location = new Point(170, 55),
                Size = new Size(145, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 150, 80),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            enableWidgetsBtn.FlatAppearance.BorderSize = 0;
            enableWidgetsBtn.Click += async (s, e) => await ToggleWidgetsAsync(true, widgetsStatus);

            widgetsGroup.Controls.Add(widgetsStatus);
            widgetsGroup.Controls.Add(disableWidgetsBtn);
            widgetsGroup.Controls.Add(enableWidgetsBtn);
            CheckWidgetsStatus(widgetsStatus);

            // --- Chat Icon Remove ---
            var chatGroup = new GroupBox
            {
                Text = Get("tweak.chat"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var chatStatus = new Label
            {
                Text = Get("common.checking"),
                Location = new Point(15, 28),
                Size = new Size(350, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var hideChatBtn = new Button
            {
                Text = Get("tweak.hide_chat"),
                Location = new Point(15, 55),
                Size = new Size(145, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            hideChatBtn.FlatAppearance.BorderSize = 0;
            hideChatBtn.Click += async (s, e) => await ToggleChatIconAsync(false, chatStatus);

            var showChatBtn = new Button
            {
                Text = Get("tweak.show_chat"),
                Location = new Point(170, 55),
                Size = new Size(145, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 150, 80),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            showChatBtn.FlatAppearance.BorderSize = 0;
            showChatBtn.Click += async (s, e) => await ToggleChatIconAsync(true, chatStatus);

            chatGroup.Controls.Add(chatStatus);
            chatGroup.Controls.Add(hideChatBtn);
            chatGroup.Controls.Add(showChatBtn);
            CheckChatIconStatus(chatStatus);

            // --- Menu Show Delay ---
            var menuDelayGroup = new GroupBox
            {
                Text = Get("tweak.menu_delay"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var menuDelayStatus = new Label
            {
                Text = Get("common.checking"),
                Location = new Point(15, 28),
                Size = new Size(350, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var optimizeMenuBtn = new Button
            {
                Text = Get("tweak.instant_menus"),
                Location = new Point(15, 55),
                Size = new Size(155, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            optimizeMenuBtn.FlatAppearance.BorderSize = 0;
            optimizeMenuBtn.Click += async (s, e) => await SetMenuShowDelayAsync("0", menuDelayStatus);

            var restoreMenuBtn = new Button
            {
                Text = Get("tweak.default_delay"),
                Location = new Point(180, 55),
                Size = new Size(145, 32),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F)
            };
            restoreMenuBtn.Click += async (s, e) => await SetMenuShowDelayAsync("400", menuDelayStatus);

            var menuNote = new Label
            {
                Text = Get("tweak.menu_note"),
                Location = new Point(15, 95),
                Size = new Size(380, 20),
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.Gray
            };

            menuDelayGroup.Controls.Add(menuDelayStatus);
            menuDelayGroup.Controls.Add(optimizeMenuBtn);
            menuDelayGroup.Controls.Add(restoreMenuBtn);
            menuDelayGroup.Controls.Add(menuNote);
            CheckMenuShowDelay(menuDelayStatus);

            // --- Shutdown Speed Optimization ---
            var shutdownGroup = new GroupBox
            {
                Text = Get("tweak.shutdown_speed"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var shutdownStatus = new Label
            {
                Text = Get("common.checking"),
                Location = new Point(15, 28),
                Size = new Size(370, 35),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var optimizeShutdownBtn = new Button
            {
                Text = Get("tweak.optimize_shutdown"),
                Location = new Point(15, 70),
                Size = new Size(190, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            optimizeShutdownBtn.FlatAppearance.BorderSize = 0;
            optimizeShutdownBtn.Click += async (s, e) => await OptimizeShutdownSpeedAsync(shutdownStatus);

            var restoreShutdownBtn = new Button
            {
                Text = Get("tweak.restore_defaults"),
                Location = new Point(215, 70),
                Size = new Size(130, 32),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F)
            };
            restoreShutdownBtn.Click += async (s, e) => await RestoreShutdownSpeedAsync(shutdownStatus);

            var shutdownNote = new Label
            {
                Text = Get("tweak.shutdown_note"),
                Location = new Point(15, 110),
                Size = new Size(380, 20),
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.Gray
            };

            shutdownGroup.Controls.Add(shutdownStatus);
            shutdownGroup.Controls.Add(optimizeShutdownBtn);
            shutdownGroup.Controls.Add(restoreShutdownBtn);
            shutdownGroup.Controls.Add(shutdownNote);
            CheckShutdownSpeed(shutdownStatus);

            // --- AutoEndTask ---
            var autoEndGroup = new GroupBox
            {
                Text = Get("tweak.autoendtask"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var autoEndStatus = new Label
            {
                Text = Get("common.checking"),
                Location = new Point(15, 28),
                Size = new Size(350, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var enableAutoEndBtn = new Button
            {
                Text = Get("tweak.enable_autoend"),
                Location = new Point(15, 55),
                Size = new Size(155, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            enableAutoEndBtn.FlatAppearance.BorderSize = 0;
            enableAutoEndBtn.Click += async (s, e) => await ToggleAutoEndTaskAsync(true, autoEndStatus);

            var disableAutoEndBtn = new Button
            {
                Text = Get("tweak.disable_autoend"),
                Location = new Point(180, 55),
                Size = new Size(155, 32),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F)
            };
            disableAutoEndBtn.Click += async (s, e) => await ToggleAutoEndTaskAsync(false, autoEndStatus);

            var autoEndNote = new Label
            {
                Text = Get("tweak.autoend_note"),
                Location = new Point(15, 95),
                Size = new Size(380, 20),
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.Gray
            };

            autoEndGroup.Controls.Add(autoEndStatus);
            autoEndGroup.Controls.Add(enableAutoEndBtn);
            autoEndGroup.Controls.Add(disableAutoEndBtn);
            autoEndGroup.Controls.Add(autoEndNote);
            CheckAutoEndTaskStatus(autoEndStatus);

            // --- Old Context Menu (Win10 style) ---
            var ctxMenuGroup = new GroupBox
            {
                Text = Get("tweak.context_menu"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var ctxMenuStatus = new Label
            {
                Text = Get("common.checking"),
                Location = new Point(15, 28),
                Size = new Size(350, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var enableOldCtxBtn = new Button
            {
                Text = Get("tweak.ctx_old"),
                Location = new Point(15, 55),
                Size = new Size(160, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            enableOldCtxBtn.FlatAppearance.BorderSize = 0;
            enableOldCtxBtn.Click += async (s, e) => await ToggleClassicContextMenuAsync(true, ctxMenuStatus);

            var restoreNewCtxBtn = new Button
            {
                Text = Get("tweak.ctx_new"),
                Location = new Point(185, 55),
                Size = new Size(160, 32),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F)
            };
            restoreNewCtxBtn.Click += async (s, e) => await ToggleClassicContextMenuAsync(false, ctxMenuStatus);

            var ctxMenuNote = new Label
            {
                Text = Get("tweak.ctx_note"),
                Location = new Point(15, 95),
                Size = new Size(380, 20),
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.Gray
            };

            ctxMenuGroup.Controls.Add(ctxMenuStatus);
            ctxMenuGroup.Controls.Add(enableOldCtxBtn);
            ctxMenuGroup.Controls.Add(restoreNewCtxBtn);
            ctxMenuGroup.Controls.Add(ctxMenuNote);
            CheckClassicContextMenuStatus(ctxMenuStatus);

            // Layout: 2 columns, 5 rows
            layout.Controls.Add(darkModeGroup, 0, 0);
            layout.Controls.Add(transparencyGroup, 1, 0);
            layout.Controls.Add(fileExtGroup, 0, 1);
            layout.Controls.Add(widgetsGroup, 1, 1);
            layout.Controls.Add(chatGroup, 0, 2);
            layout.Controls.Add(menuDelayGroup, 1, 2);
            layout.Controls.Add(shutdownGroup, 0, 3);
            layout.Controls.Add(autoEndGroup, 1, 3);
            layout.Controls.Add(ctxMenuGroup, 0, 4);

            tab.Controls.Add(layout);
            return tab;
        }

        #endregion

        #region Tweaks Operations

        // --- Dark / Light Mode ---
        private void CheckDarkModeStatus(Label label)
        {
            try
            {
                var result = _registry.GetValue(
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme");
                if (result.Success && result.Data != null)
                {
                    int val = Convert.ToInt32(result.Data);
                    label.Text = val == 0
                        ? Get("tweak.dark_active")
                        : Get("tweak.light_active");
                    label.ForeColor = val == 0 ? Color.Green : Color.DarkOrange;
                }
                else
                {
                    label.Text = Get("tweak.dark_default");
                    label.ForeColor = Color.Gray;
                }
            }
            catch
            {
                label.Text = Get("common.unable_determine");
                label.ForeColor = Color.Gray;
            }
        }

        private async Task ToggleDarkModeAsync(bool enableDark, Label statusLabel)
        {
            int value = enableDark ? 0 : 1;
            string mode = enableDark ? Get("word.dark") : Get("word.light");

            var changes = new List<string>
            {
                $"Registry: HKCU\\...\\Themes\\Personalize\\AppsUseLightTheme = {value}",
                $"Registry: HKCU\\...\\Themes\\Personalize\\SystemUsesLightTheme = {value}",
                Get("tweak.op.switches_to", mode)
            };

            if (!PreviewDialog.ShowPreview(this, Get("tweak.op.switch_mode", mode), changes))
                return;

            await ExecuteWithSafetyAsync(Get("tweak.op.switch_mode", mode), async () =>
            {
                var backups = new List<string>();

                var r1 = _registry.SetValueWithBackup(
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", value, RegistryValueKind.DWord);
                if (r1.Success) backups.Add(r1.Data!);

                var r2 = _registry.SetValueWithBackup(
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "SystemUsesLightTheme", value, RegistryValueKind.DWord);
                if (r2.Success) backups.Add(r2.Data!);

                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("tweak.op.revert_mode", enableDark ? Get("word.light") : Get("word.dark")),
                    BackupFiles = backups
                });
                UpdateUndoButton();

                CheckDarkModeStatus(statusLabel);
                SetStatus(Get("tweak.op.switched_to", mode), false);
                await Task.CompletedTask;
            });
        }

        // --- Transparency Toggle ---
        private void CheckTransparencyStatus(Label label)
        {
            try
            {
                var result = _registry.GetValue(
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "EnableTransparency");
                if (result.Success && result.Data != null)
                {
                    int val = Convert.ToInt32(result.Data);
                    label.Text = val == 1
                        ? Get("tweak.trans_enabled")
                        : Get("tweak.trans_disabled");
                    label.ForeColor = val == 1 ? Color.Green : Color.DarkOrange;
                }
                else
                {
                    label.Text = Get("tweak.trans_default");
                    label.ForeColor = Color.Gray;
                }
            }
            catch
            {
                label.Text = Get("common.unable_determine");
                label.ForeColor = Color.Gray;
            }
        }

        private async Task ToggleTransparencyAsync(bool enable, Label statusLabel)
        {
            int value = enable ? 1 : 0;
            string action = enable ? Get("common.enable") : Get("common.disable");

            var changes = new List<string>
            {
                $"Registry: HKCU\\...\\Themes\\Personalize\\EnableTransparency = {value}",
                enable
                    ? Get("tweak.op.trans_enable_desc")
                    : Get("tweak.op.trans_disable_desc")
            };

            if (!PreviewDialog.ShowPreview(this, $"{action} Transparency", changes))
                return;

            await ExecuteWithSafetyAsync($"{action} Transparency", async () =>
            {
                var backups = new List<string>();

                var r1 = _registry.SetValueWithBackup(
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "EnableTransparency", value, RegistryValueKind.DWord);
                if (r1.Success) backups.Add(r1.Data!);

                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("tweak.op.revert_trans", enable ? Get("word.disabled") : Get("word.enabled")),
                    BackupFiles = backups
                });
                UpdateUndoButton();

                CheckTransparencyStatus(statusLabel);
                SetStatus(Get("tweak.op.trans_set", enable ? Get("word.enabled") : Get("word.disabled")), false);
                await Task.CompletedTask;
            });
        }

        // --- Show File Extensions ---
        private void CheckFileExtensionsStatus(Label label)
        {
            try
            {
                var result = _registry.GetValue(
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    "HideFileExt");
                if (result.Success && result.Data != null)
                {
                    int val = Convert.ToInt32(result.Data);
                    label.Text = val == 0
                        ? Get("tweak.ext_visible")
                        : Get("tweak.ext_hidden");
                    label.ForeColor = val == 0 ? Color.Green : Color.DarkOrange;
                }
                else
                {
                    label.Text = Get("tweak.ext_default");
                    label.ForeColor = Color.Gray;
                }
            }
            catch
            {
                label.Text = Get("common.unable_determine");
                label.ForeColor = Color.Gray;
            }
        }

        private async Task ToggleFileExtensionsAsync(bool show, Label statusLabel)
        {
            int value = show ? 0 : 1; // 0 = show, 1 = hide
            string action = show ? Get("common.show") : Get("common.hide");

            var changes = new List<string>
            {
                $"Registry: HKCU\\...\\Explorer\\Advanced\\HideFileExt = {value}",
                show
                    ? Get("tweak.op.ext_show_desc")
                    : Get("tweak.op.ext_hide_desc")
            };

            if (!PreviewDialog.ShowPreview(this, $"{action} File Extensions", changes))
                return;

            await ExecuteWithSafetyAsync($"{action} File Extensions", async () =>
            {
                var backups = new List<string>();

                var r1 = _registry.SetValueWithBackup(
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    "HideFileExt", value, RegistryValueKind.DWord);
                if (r1.Success) backups.Add(r1.Data!);

                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("tweak.op.revert_ext", show ? Get("word.hidden") : Get("word.visible")),
                    BackupFiles = backups
                });
                UpdateUndoButton();

                CheckFileExtensionsStatus(statusLabel);
                SetStatus(Get("tweak.op.ext_set", show ? Get("word.shown") : Get("word.hidden")), false);
                await Task.CompletedTask;
            });
        }

        // --- Widgets ---
        private void CheckWidgetsStatus(Label label)
        {
            try
            {
                var result = _registry.GetValue(
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    "TaskbarDa");
                if (result.Success && result.Data != null)
                {
                    int val = Convert.ToInt32(result.Data);
                    label.Text = val == 0
                        ? Get("tweak.widgets_disabled")
                        : Get("tweak.widgets_enabled");
                    label.ForeColor = val == 0 ? Color.Green : Color.DarkOrange;
                }
                else
                {
                    label.Text = Get("tweak.widgets_default");
                    label.ForeColor = Color.Gray;
                }
            }
            catch
            {
                label.Text = Get("common.unable_determine");
                label.ForeColor = Color.Gray;
            }
        }

        private async Task ToggleWidgetsAsync(bool enable, Label statusLabel)
        {
            int value = enable ? 1 : 0;
            string action = enable ? Get("common.enable") : Get("common.disable");

            var changes = new List<string>
            {
                $"Registry: HKCU\\...\\Explorer\\Advanced\\TaskbarDa = {value}",
                enable
                    ? Get("tweak.op.widgets_enable_desc")
                    : Get("tweak.op.widgets_disable_desc")
            };

            if (!PreviewDialog.ShowPreview(this, $"{action} Widgets", changes))
                return;

            await ExecuteWithSafetyAsync($"{action} Widgets", async () =>
            {
                var backups = new List<string>();

                var r1 = _registry.SetValueWithBackup(
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    "TaskbarDa", value, RegistryValueKind.DWord);
                if (r1.Success) backups.Add(r1.Data!);

                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("tweak.op.revert_widgets", enable ? Get("word.disabled") : Get("word.enabled")),
                    BackupFiles = backups
                });
                UpdateUndoButton();

                CheckWidgetsStatus(statusLabel);
                SetStatus(Get("tweak.op.widgets_set", enable ? Get("word.enabled") : Get("word.disabled")), false);
                await Task.CompletedTask;
            });
        }

        // --- Chat Icon ---
        private void CheckChatIconStatus(Label label)
        {
            try
            {
                var result = _registry.GetValue(
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    "TaskbarMn");
                if (result.Success && result.Data != null)
                {
                    int val = Convert.ToInt32(result.Data);
                    label.Text = val == 0
                        ? Get("tweak.chat_hidden")
                        : Get("tweak.chat_visible");
                    label.ForeColor = val == 0 ? Color.Green : Color.DarkOrange;
                }
                else
                {
                    label.Text = Get("tweak.chat_default");
                    label.ForeColor = Color.Gray;
                }
            }
            catch
            {
                label.Text = Get("common.unable_determine");
                label.ForeColor = Color.Gray;
            }
        }

        private async Task ToggleChatIconAsync(bool show, Label statusLabel)
        {
            int value = show ? 1 : 0;
            string action = show ? Get("common.show") : Get("common.hide");

            var changes = new List<string>
            {
                $"Registry: HKCU\\...\\Explorer\\Advanced\\TaskbarMn = {value}",
                show
                    ? Get("tweak.op.chat_show_desc")
                    : Get("tweak.op.chat_hide_desc")
            };

            if (!PreviewDialog.ShowPreview(this, $"{action} Chat Icon", changes))
                return;

            await ExecuteWithSafetyAsync($"{action} Chat Icon", async () =>
            {
                var backups = new List<string>();

                var r1 = _registry.SetValueWithBackup(
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    "TaskbarMn", value, RegistryValueKind.DWord);
                if (r1.Success) backups.Add(r1.Data!);

                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("tweak.op.revert_chat", show ? Get("word.hidden") : Get("word.visible")),
                    BackupFiles = backups
                });
                UpdateUndoButton();

                CheckChatIconStatus(statusLabel);
                SetStatus(Get("tweak.op.chat_set", show ? Get("word.shown") : Get("word.hidden")), false);
                await Task.CompletedTask;
            });
        }

        // --- Menu Show Delay ---
        private void CheckMenuShowDelay(Label label)
        {
            try
            {
                var result = _registry.GetValue(
                    @"HKCU\Control Panel\Desktop",
                    "MenuShowDelay");
                if (result.Success && result.Data != null)
                {
                    string val = result.Data.ToString() ?? "400";
                    label.Text = Get("tweak.menu_status", val);
                    label.ForeColor = val == "0" ? Color.Green
                        : int.TryParse(val, out int ms) && ms <= 50 ? Color.Green
                        : Color.DarkOrange;
                }
                else
                {
                    label.Text = Get("tweak.menu_default");
                    label.ForeColor = Color.Gray;
                }
            }
            catch
            {
                label.Text = Get("common.unable_determine");
                label.ForeColor = Color.Gray;
            }
        }

        private async Task SetMenuShowDelayAsync(string delayMs, Label statusLabel)
        {
            var changes = new List<string>
            {
                $"Registry: HKCU\\Control Panel\\Desktop\\MenuShowDelay = {delayMs}",
                delayMs == "0"
                    ? Get("tweak.op.menu_instant")
                    : Get("tweak.op.menu_delay_set", delayMs)
            };

            if (!PreviewDialog.ShowPreview(this, Get("tweak.op.set_menu_title", delayMs), changes))
                return;

            await ExecuteWithSafetyAsync(Get("tweak.op.set_menu_title", delayMs), async () =>
            {
                var backups = new List<string>();

                var r1 = _registry.SetValueWithBackup(
                    @"HKCU\Control Panel\Desktop",
                    "MenuShowDelay", delayMs, RegistryValueKind.String);
                if (r1.Success) backups.Add(r1.Data!);

                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("tweak.op.revert_menu"),
                    BackupFiles = backups
                });
                UpdateUndoButton();

                CheckMenuShowDelay(statusLabel);
                SetStatus(Get("tweak.op.menu_set", delayMs), false);
                await Task.CompletedTask;
            });
        }

        // --- Shutdown Speed ---
        private void CheckShutdownSpeed(Label label)
        {
            try
            {
                var r1 = _registry.GetValue(
                    @"HKCU\Control Panel\Desktop",
                    "WaitToKillAppTimeout");
                var r2 = _registry.GetValue(
                    @"HKLM\SYSTEM\CurrentControlSet\Control",
                    "WaitToKillServiceTimeout");

                string appTimeout = r1.Success && r1.Data != null ? r1.Data.ToString()! : "20000";
                string svcTimeout = r2.Success && r2.Data != null ? r2.Data.ToString()! : "5000";

                label.Text = Get("tweak.shutdown_status", appTimeout, svcTimeout);
                bool optimized = int.TryParse(appTimeout, out int a) && a <= 2000
                              && int.TryParse(svcTimeout, out int s) && s <= 2000;
                label.ForeColor = optimized ? Color.Green : Color.DarkOrange;
            }
            catch
            {
                label.Text = Get("common.unable_determine");
                label.ForeColor = Color.Gray;
            }
        }

        private async Task OptimizeShutdownSpeedAsync(Label statusLabel)
        {
            var changes = new List<string>
            {
                "Registry: HKCU\\Control Panel\\Desktop\\WaitToKillAppTimeout = 2000 (was 20000)",
                "Registry: HKLM\\SYSTEM\\CurrentControlSet\\Control\\WaitToKillServiceTimeout = 2000 (was 5000)",
                "Registry: HKCU\\Control Panel\\Desktop\\HungAppTimeout = 1000 (was 5000)",
                Get("tweak.op.shutdown_reduces")
            };

            if (!PreviewDialog.ShowPreview(this, Get("tweak.op.shutdown_title"), changes))
                return;

            await ExecuteWithSafetyAsync(Get("tweak.op.shutdown_title"), async () =>
            {
                var backups = new List<string>();

                var r1 = _registry.SetValueWithBackup(
                    @"HKCU\Control Panel\Desktop",
                    "WaitToKillAppTimeout", "2000", RegistryValueKind.String);
                if (r1.Success) backups.Add(r1.Data!);

                var r2 = _registry.SetValueWithBackup(
                    @"HKLM\SYSTEM\CurrentControlSet\Control",
                    "WaitToKillServiceTimeout", "2000", RegistryValueKind.String);
                if (r2.Success) backups.Add(r2.Data!);

                var r3 = _registry.SetValueWithBackup(
                    @"HKCU\Control Panel\Desktop",
                    "HungAppTimeout", "1000", RegistryValueKind.String);
                if (r3.Success) backups.Add(r3.Data!);

                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("tweak.op.revert_shutdown"),
                    BackupFiles = backups
                });
                UpdateUndoButton();

                CheckShutdownSpeed(statusLabel);
                SetStatus(Get("tweak.op.shutdown_optimized"), false);
                await Task.CompletedTask;
            });
        }

        private async Task RestoreShutdownSpeedAsync(Label statusLabel)
        {
            var changes = new List<string>
            {
                "Registry: HKCU\\Control Panel\\Desktop\\WaitToKillAppTimeout = 20000 (default)",
                "Registry: HKLM\\SYSTEM\\CurrentControlSet\\Control\\WaitToKillServiceTimeout = 5000 (default)",
                "Registry: HKCU\\Control Panel\\Desktop\\HungAppTimeout = 5000 (default)",
                Get("tweak.op.shutdown_restores")
            };

            if (!PreviewDialog.ShowPreview(this, Get("tweak.op.restore_shutdown_title"), changes))
                return;

            await ExecuteWithSafetyAsync(Get("tweak.op.restore_shutdown_title"), async () =>
            {
                var backups = new List<string>();

                var r1 = _registry.SetValueWithBackup(
                    @"HKCU\Control Panel\Desktop",
                    "WaitToKillAppTimeout", "20000", RegistryValueKind.String);
                if (r1.Success) backups.Add(r1.Data!);

                var r2 = _registry.SetValueWithBackup(
                    @"HKLM\SYSTEM\CurrentControlSet\Control",
                    "WaitToKillServiceTimeout", "5000", RegistryValueKind.String);
                if (r2.Success) backups.Add(r2.Data!);

                var r3 = _registry.SetValueWithBackup(
                    @"HKCU\Control Panel\Desktop",
                    "HungAppTimeout", "5000", RegistryValueKind.String);
                if (r3.Success) backups.Add(r3.Data!);

                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("tweak.op.revert_shutdown_optimized"),
                    BackupFiles = backups
                });
                UpdateUndoButton();

                CheckShutdownSpeed(statusLabel);
                SetStatus(Get("tweak.op.shutdown_restored"), false);
                await Task.CompletedTask;
            });
        }

        // --- AutoEndTask ---
        private void CheckAutoEndTaskStatus(Label label)
        {
            try
            {
                var result = _registry.GetValue(
                    @"HKCU\Control Panel\Desktop",
                    "AutoEndTasks");
                if (result.Success && result.Data != null)
                {
                    string val = result.Data.ToString() ?? "0";
                    label.Text = val == "1"
                        ? Get("tweak.autoend_enabled")
                        : Get("tweak.autoend_disabled");
                    label.ForeColor = val == "1" ? Color.Green : Color.DarkOrange;
                }
                else
                {
                    label.Text = Get("tweak.autoend_default");
                    label.ForeColor = Color.Gray;
                }
            }
            catch
            {
                label.Text = Get("common.unable_determine");
                label.ForeColor = Color.Gray;
            }
        }

        private async Task ToggleAutoEndTaskAsync(bool enable, Label statusLabel)
        {
            string value = enable ? "1" : "0";
            string action = enable ? Get("common.enable") : Get("common.disable");

            var changes = new List<string>
            {
                $"Registry: HKCU\\Control Panel\\Desktop\\AutoEndTasks = {value}",
                enable
                    ? Get("tweak.op.autoend_enable_desc")
                    : Get("tweak.op.autoend_disable_desc")
            };

            if (!PreviewDialog.ShowPreview(this, $"{action} AutoEndTask", changes))
                return;

            await ExecuteWithSafetyAsync($"{action} AutoEndTask", async () =>
            {
                var backups = new List<string>();

                var r1 = _registry.SetValueWithBackup(
                    @"HKCU\Control Panel\Desktop",
                    "AutoEndTasks", value, RegistryValueKind.String);
                if (r1.Success) backups.Add(r1.Data!);

                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("tweak.op.revert_autoend", enable ? Get("word.disabled") : Get("word.enabled")),
                    BackupFiles = backups
                });
                UpdateUndoButton();

                CheckAutoEndTaskStatus(statusLabel);
                SetStatus(Get("tweak.op.autoend_set", enable ? Get("word.enabled") : Get("word.disabled")), false);
                await Task.CompletedTask;
            });
        }

        // --- Classic (Win10) Context Menu ---
        private const string CtxMenuRegPath = @"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";

        private void CheckClassicContextMenuStatus(Label label)
        {
            try
            {
                var result = _registry.GetValue(CtxMenuRegPath, "");
                if (result.Success && result.Data is string s && s == "")
                    label.Text = Get("tweak.ctx_old_active");
                else
                    label.Text = Get("tweak.ctx_new_active");
            }
            catch { label.Text = Get("tweak.ctx_new_active"); }
        }

        private async Task ToggleClassicContextMenuAsync(bool useClassic, Label statusLabel)
        {
            string action = useClassic ? Get("tweak.ctx_old") : Get("tweak.ctx_new");
            string desc = useClassic ? Get("tweak.op.ctx_old_desc") : Get("tweak.op.ctx_new_desc");

            var changes = new List<string>
            {
                desc,
                Get("tweak.op.ctx_restart_explorer")
            };

            if (!PreviewDialog.ShowPreview(this, action, changes))
                return;

            await ExecuteWithSafetyAsync(action, async () =>
            {
                var backups = new List<string>();

                if (useClassic)
                {
                    // Create the key with empty default value to force classic menus
                    var r = _registry.SetValueWithBackup(CtxMenuRegPath, "", "", RegistryValueKind.String);
                    if (r.Success) backups.Add(r.Data!);
                }
                else
                {
                    // Export backup then delete the key to restore Win11 menus
                    try
                    {
                        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                            @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", false);
                        if (key != null)
                            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
                                @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", false);
                    }
                    catch { /* key may not exist */ }
                }

                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("tweak.op.revert_ctx", useClassic ? Get("tweak.ctx_new") : Get("tweak.ctx_old")),
                    BackupFiles = backups
                });
                UpdateUndoButton();

                // Restart Explorer so the change takes effect immediately
                try
                {
                    foreach (var proc in System.Diagnostics.Process.GetProcessesByName("explorer"))
                        proc.Kill();
                    await Task.Delay(500);
                    System.Diagnostics.Process.Start("explorer.exe");
                }
                catch { /* Explorer will auto-restart */ }

                await Task.Delay(1500);
                CheckClassicContextMenuStatus(statusLabel);
                SetStatus(Get("tweak.op.ctx_set", useClassic ? Get("tweak.ctx_old") : Get("tweak.ctx_new")), false);
            });
        }

        #endregion

        #region Apps Tab

        private TabPage BuildAppsTab()
        {
            var tab = new TabPage(Get("tab.apps"));
            tab.Padding = new Padding(10);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 75F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));

            // --- App List Panel ---
            var appListPanel = new Panel { Dock = DockStyle.Fill };

            // Filter / toolbar
            var filterPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40
            };

            var filterLabel = new Label
            {
                Text = Get("apps.show"),
                Location = new Point(0, 8),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F)
            };

            var filterCombo = new ComboBox
            {
                Location = new Point(45, 5),
                Size = new Size(170, 28),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9F)
            };
            filterCombo.Items.AddRange(new object[]
            {
                Get("apps.all_store"),
                Get("apps.bloatware_only"),
                Get("apps.desktop_apps")
            });
            filterCombo.SelectedIndex = 0;

            var searchBox = new TextBox
            {
                Location = new Point(230, 5),
                Size = new Size(200, 28),
                Font = new Font("Segoe UI", 9F),
                PlaceholderText = Get("apps.search")
            };

            var refreshBtn = new Button
            {
                Text = Get("apps.refresh"),
                Location = new Point(445, 4),
                Size = new Size(90, 28),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F),
                Cursor = Cursors.Hand
            };

            var selectAllCheck = new CheckBox
            {
                Text = Get("common.select_all"),
                Location = new Point(550, 7),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F)
            };

            filterPanel.Controls.Add(filterLabel);
            filterPanel.Controls.Add(filterCombo);
            filterPanel.Controls.Add(searchBox);
            filterPanel.Controls.Add(refreshBtn);
            filterPanel.Controls.Add(selectAllCheck);

            // App ListView
            var appListView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                CheckBoxes = true,
                GridLines = true,
                Font = new Font("Segoe UI", 9F),
                MultiSelect = true
            };
            appListView.Columns.Add(Get("apps.col_name"), 230);
            appListView.Columns.Add(Get("apps.col_publisher"), 140);
            appListView.Columns.Add(Get("apps.col_version"), 80);
            appListView.Columns.Add(Get("apps.col_type"), 70);
            appListView.Columns.Add(Get("apps.col_size"), 70);

            var appCountLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 20,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.Gray,
                Text = Get("apps.loading")
            };

            appListPanel.Controls.Add(appListView);
            appListPanel.Controls.Add(appCountLabel);
            appListPanel.Controls.Add(filterPanel);

            // --- Action Panel ---
            var actionGroup = new GroupBox
            {
                Text = Get("apps.actions"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var uninstallSelectedBtn = new Button
            {
                Text = Get("apps.uninstall_selected"),
                Location = new Point(15, 25),
                Size = new Size(175, 35),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            uninstallSelectedBtn.FlatAppearance.BorderSize = 0;

            var bloatwareBtn = new Button
            {
                Text = Get("apps.bloatware_remover"),
                Location = new Point(200, 25),
                Size = new Size(240, 35),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            bloatwareBtn.FlatAppearance.BorderSize = 0;

            var removeAllUsersBtn = new Button
            {
                Text = Get("apps.remove_all_users"),
                Location = new Point(450, 25),
                Size = new Size(165, 35),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(180, 80, 0),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F),
                Cursor = Cursors.Hand
            };
            removeAllUsersBtn.FlatAppearance.BorderSize = 0;

            var appProgressBar = new ProgressBar
            {
                Location = new Point(15, 72),
                Size = new Size(600, 16),
                Style = ProgressBarStyle.Continuous,
                Visible = false
            };

            var appStatusLabel = new Label
            {
                Text = "",
                Location = new Point(15, 92),
                Size = new Size(600, 20),
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.Gray
            };

            var warningLabel = new Label
            {
                Text = Get("apps.protected_warning"),
                Location = new Point(625, 25),
                Size = new Size(240, 45),
                Font = new Font("Segoe UI", 7.5F),
                ForeColor = Color.DarkOrange
            };

            actionGroup.Controls.Add(uninstallSelectedBtn);
            actionGroup.Controls.Add(bloatwareBtn);
            actionGroup.Controls.Add(removeAllUsersBtn);
            actionGroup.Controls.Add(appProgressBar);
            actionGroup.Controls.Add(appStatusLabel);
            actionGroup.Controls.Add(warningLabel);

            layout.Controls.Add(appListPanel, 0, 0);
            layout.Controls.Add(actionGroup, 0, 1);

            tab.Controls.Add(layout);

            // --- Store loaded app data for filtering ---
            var allItems = new List<ListViewItem>();

            // --- Event Handlers ---

            // Load apps on filter change
            filterCombo.SelectedIndexChanged += async (s, e) =>
            {
                await LoadAppsAsync(filterCombo, appListView, appCountLabel, searchBox, allItems);
            };

            // Search filter
            searchBox.TextChanged += (s, e) =>
            {
                FilterAppList(appListView, allItems, searchBox.Text);
                UpdateAppCount(appListView, appCountLabel);
            };

            // Refresh
            refreshBtn.Click += async (s, e) =>
            {
                await LoadAppsAsync(filterCombo, appListView, appCountLabel, searchBox, allItems);
            };

            // Select all
            selectAllCheck.CheckedChanged += (s, e) =>
            {
                foreach (ListViewItem item in appListView.Items)
                    item.Checked = selectAllCheck.Checked;
            };

            // Uninstall selected
            uninstallSelectedBtn.Click += async (s, e) =>
            {
                await UninstallSelectedAppsAsync(appListView, appProgressBar, appStatusLabel,
                    filterCombo, appCountLabel, searchBox, allItems);
            };

            // One-click bloatware remover
            bloatwareBtn.Click += async (s, e) =>
            {
                await OneClickBloatwareRemoveAsync(appProgressBar, appStatusLabel,
                    filterCombo, appListView, appCountLabel, searchBox, allItems);
            };

            // Remove for all users
            removeAllUsersBtn.Click += async (s, e) =>
            {
                await RemoveSelectedForAllUsersAsync(appListView, appProgressBar, appStatusLabel);
            };

            // Initial load
            tab.Enter += async (s, e) =>
            {
                if (allItems.Count == 0)
                    await LoadAppsAsync(filterCombo, appListView, appCountLabel, searchBox, allItems);
            };

            return tab;
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
            changes.Add(Get("apps.op.bloat_provisioned"));

            if (!PreviewDialog.ShowPreview(this, Get("apps.op.bloat_title"), changes,
                Get("apps.op.bloat_warning")))
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

            var result = await _appManager.RemoveBloatwareAsync(progress);
            progressBar.Visible = false;

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

            progressBar.Visible = true;
            progressBar.Maximum = storeApps.Count;
            progressBar.Value = 0;

            int succeeded = 0;
            int failed = 0;

            foreach (var app in storeApps)
            {
                statusLabel.Text = Get("apps.op.removing_all", app.Name);
                statusLabel.ForeColor = _isDarkMode ? DarkText : Color.Gray;

                // Remove for current user first
                await _appManager.UninstallStoreAppAsync(app);

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
            statusLabel.Text = Get("apps.op.done_all", succeeded, failed);
            statusLabel.ForeColor = failed == 0 ? Color.Green : Color.DarkOrange;
            SetStatus(Get("apps.op.removed_all", succeeded), failed > 0);
        }

        #endregion

        #region RAM Tab

        private TabPage BuildRamTab()
        {
            var tab = new TabPage(Get("tab.ram"));
            tab.Padding = new Padding(10);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                AutoScroll = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            // --- RAM Usage Info (Real-time) ---
            var ramInfoGroup = new GroupBox
            {
                Text = Get("ram.usage_live"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var ramUsageBar = new ProgressBar
            {
                Location = new Point(15, 28),
                Size = new Size(350, 22),
                Style = ProgressBarStyle.Continuous,
                Maximum = 100
            };

            var ramPercentLabel = new Label
            {
                Text = "...",
                Location = new Point(370, 28),
                Size = new Size(50, 22),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var ramDetailLabel = new Label
            {
                Text = Get("common.loading"),
                Location = new Point(15, 55),
                Size = new Size(400, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var ramPageFileLabel = new Label
            {
                Text = "",
                Location = new Point(15, 78),
                Size = new Size(400, 20),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.Gray
            };

            var ramPageConfigLabel = new Label
            {
                Text = "",
                Location = new Point(15, 98),
                Size = new Size(400, 35),
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = Color.Gray
            };

            ramInfoGroup.Controls.Add(ramUsageBar);
            ramInfoGroup.Controls.Add(ramPercentLabel);
            ramInfoGroup.Controls.Add(ramDetailLabel);
            ramInfoGroup.Controls.Add(ramPageFileLabel);
            ramInfoGroup.Controls.Add(ramPageConfigLabel);

            // --- Clear Cached RAM ---
            var clearRamGroup = new GroupBox
            {
                Text = Get("ram.clear_cached"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var clearRamBtn = new Button
            {
                Text = Get("ram.empty_working"),
                Location = new Point(15, 28),
                Size = new Size(200, 35),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            clearRamBtn.FlatAppearance.BorderSize = 0;

            var clearRamStatus = new Label
            {
                Text = Get("ram.frees_cached"),
                Location = new Point(15, 70),
                Size = new Size(400, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var clearRamNote = new Label
            {
                Text = Get("ram.safe_note"),
                Location = new Point(15, 95),
                Size = new Size(400, 35),
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.Gray
            };

            clearRamBtn.Click += async (s, e) =>
                await ClearCachedRamAsync(clearRamStatus, ramUsageBar, ramPercentLabel, ramDetailLabel);

            clearRamGroup.Controls.Add(clearRamBtn);
            clearRamGroup.Controls.Add(clearRamStatus);
            clearRamGroup.Controls.Add(clearRamNote);

            // --- Top Processes by RAM ---
            var topProcsGroup = new GroupBox
            {
                Text = Get("ram.top_processes"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var processListView = new ListView
            {
                Location = new Point(15, 25),
                Size = new Size(400, 130),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular)
            };
            processListView.Columns.Add(Get("ram.col_process"), 180);
            processListView.Columns.Add(Get("ram.col_ram_mb"), 80);
            processListView.Columns.Add(Get("ram.col_private_mb"), 85);
            processListView.Columns.Add(Get("ram.col_pid"), 55);

            var refreshProcsBtn = new Button
            {
                Text = Get("apps.refresh"),
                Location = new Point(15, 160),
                Size = new Size(90, 28),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F),
                Cursor = Cursors.Hand
            };
            refreshProcsBtn.Click += (s, e) => LoadTopProcesses(processListView);

            topProcsGroup.Controls.Add(processListView);
            topProcsGroup.Controls.Add(refreshProcsBtn);

            // --- SysMain / Superfetch ---
            var sysMainGroup = new GroupBox
            {
                Text = Get("ram.sysmain"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var sysMainStatus = new Label
            {
                Text = Get("common.checking"),
                Location = new Point(15, 28),
                Size = new Size(350, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var enableSysMainBtn = new Button
            {
                Text = Get("ram.enable_sysmain"),
                Location = new Point(15, 55),
                Size = new Size(135, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 150, 80),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            enableSysMainBtn.FlatAppearance.BorderSize = 0;
            enableSysMainBtn.Click += async (s, e) => await ToggleSysMainAsync(true, sysMainStatus);

            var disableSysMainBtn = new Button
            {
                Text = Get("ram.disable_sysmain"),
                Location = new Point(160, 55),
                Size = new Size(135, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            disableSysMainBtn.FlatAppearance.BorderSize = 0;
            disableSysMainBtn.Click += async (s, e) => await ToggleSysMainAsync(false, sysMainStatus);

            var sysMainNote = new Label
            {
                Text = Get("ram.sysmain_note"),
                Location = new Point(15, 95),
                Size = new Size(380, 35),
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.Gray
            };

            sysMainGroup.Controls.Add(sysMainStatus);
            sysMainGroup.Controls.Add(enableSysMainBtn);
            sysMainGroup.Controls.Add(disableSysMainBtn);
            sysMainGroup.Controls.Add(sysMainNote);
            CheckSysMainStatus(sysMainStatus);

            // --- Memory Compression Toggle ---
            var compressionGroup = new GroupBox
            {
                Text = Get("ram.compression"),
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            var compressionStatus = new Label
            {
                Text = Get("common.checking"),
                Location = new Point(15, 28),
                Size = new Size(350, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var enableCompBtn = new Button
            {
                Text = Get("ram.enable_compression"),
                Location = new Point(15, 55),
                Size = new Size(155, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 150, 80),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            enableCompBtn.FlatAppearance.BorderSize = 0;
            enableCompBtn.Click += async (s, e) => await ToggleMemoryCompressionAsync(true, compressionStatus);

            var disableCompBtn = new Button
            {
                Text = Get("ram.disable_compression"),
                Location = new Point(180, 55),
                Size = new Size(155, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            disableCompBtn.FlatAppearance.BorderSize = 0;
            disableCompBtn.Click += async (s, e) => await ToggleMemoryCompressionAsync(false, compressionStatus);

            var compressionNote = new Label
            {
                Text = Get("ram.compression_note"),
                Location = new Point(15, 95),
                Size = new Size(380, 35),
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.DarkOrange
            };

            compressionGroup.Controls.Add(compressionStatus);
            compressionGroup.Controls.Add(enableCompBtn);
            compressionGroup.Controls.Add(disableCompBtn);
            compressionGroup.Controls.Add(compressionNote);
            CheckMemoryCompressionStatus(compressionStatus);

            // Layout
            layout.Controls.Add(ramInfoGroup, 0, 0);
            layout.Controls.Add(clearRamGroup, 1, 0);
            layout.Controls.Add(topProcsGroup, 0, 1);
            layout.SetRowSpan(topProcsGroup, 2);
            layout.Controls.Add(sysMainGroup, 1, 1);
            layout.Controls.Add(compressionGroup, 1, 2);

            tab.Controls.Add(layout);

            // Initial load
            UpdateRamDisplay(ramUsageBar, ramPercentLabel, ramDetailLabel, ramPageFileLabel, ramPageConfigLabel);
            LoadTopProcesses(processListView);

            // Start live RAM monitor timer
            _ramTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _ramTimer.Tick += (s, e) =>
            {
                if (_tabControl.SelectedTab == tab)
                    UpdateRamDisplay(ramUsageBar, ramPercentLabel, ramDetailLabel, ramPageFileLabel, ramPageConfigLabel);
            };
            _ramTimer.Start();

            return tab;
        }

        #endregion

        #region RAM Operations

        private void UpdateRamDisplay(ProgressBar bar, Label percentLabel, Label detailLabel,
            Label pageFileLabel, Label pageConfigLabel)
        {
            var result = MemoryManager.GetMemoryInfo();
            if (!result.Success) return;

            var info = result.Data!;
            bar.Value = Math.Min((int)info.MemoryLoadPercent, 100);
            percentLabel.Text = $"{info.MemoryLoadPercent}%";
            percentLabel.ForeColor = info.MemoryLoadPercent > 85 ? Color.Red
                : info.MemoryLoadPercent > 70 ? Color.DarkOrange
                : Color.Green;

            detailLabel.Text = Get("ram.used_info", info.UsedFormatted, info.TotalFormatted, info.AvailableFormatted);

            // Page file
            var pfResult = MemoryManager.GetPageFileSizeInfo();
            if (pfResult.Success)
                pageFileLabel.Text = pfResult.Data!;

            var pfConfig = MemoryManager.GetPageFileInfo();
            if (pfConfig.Success)
            {
                var (autoManaged, config) = pfConfig.Data;
                pageConfigLabel.Text = autoManaged
                    ? Get("ram.pagefile_auto", config)
                    : Get("ram.pagefile_custom", config);
            }
        }

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

        private async Task ClearCachedRamAsync(Label statusLabel, ProgressBar ramBar,
            Label percentLabel, Label detailLabel)
        {
            var changes = new List<string>
            {
                Get("ram.op.empty_calls"),
                Get("ram.op.empty_frees"),
                Get("ram.op.empty_safe"),
                Get("ram.op.empty_warning")
            };

            if (!PreviewDialog.ShowPreview(this, Get("ram.op.empty_title"), changes))
                return;

            SetBusy(true, Get("ram.op.clearing"));
            try
            {
                var result = await Task.Run(() => MemoryManager.EmptyAllWorkingSets());
                if (result.Success)
                {
                    var (cleared, freed) = result.Data;
                    statusLabel.Text = Get("ram.op.cleared", cleared, freed);
                    statusLabel.ForeColor = Color.Green;
                    SetStatus(Get("ram.op.ram_cleared", cleared, freed), false);
                }
                else
                {
                    statusLabel.Text = Get("failed", result.ErrorMessage ?? "");
                    statusLabel.ForeColor = Color.Red;
                }

                // Refresh display after a brief delay
                await Task.Delay(500);
                var info = MemoryManager.GetMemoryInfo();
                if (info.Success)
                {
                    ramBar.Value = Math.Min((int)info.Data!.MemoryLoadPercent, 100);
                    percentLabel.Text = $"{info.Data.MemoryLoadPercent}%";
                    detailLabel.Text = Get("ram.used_info", info.Data.UsedFormatted, info.Data.TotalFormatted, info.Data.AvailableFormatted);
                }
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void CheckSysMainStatus(Label label)
        {
            try
            {
                var result = MemoryManager.GetSysMainStatus();
                if (result.Success)
                {
                    var (status, startType) = result.Data;
                    bool running = status == "Running";
                    label.Text = Get("ram.sysmain_status", status, startType);
                    label.ForeColor = running ? Color.Green : Color.DarkOrange;
                }
                else
                {
                    label.Text = $"Status: {result.ErrorMessage}";
                    label.ForeColor = Color.Gray;
                }
            }
            catch
            {
                label.Text = Get("common.unable_determine");
                label.ForeColor = Color.Gray;
            }
        }

        private async Task ToggleSysMainAsync(bool enable, Label statusLabel)
        {
            string action = enable ? Get("common.enable") : Get("common.disable");

            var changes = new List<string>
            {
                enable
                    ? "Registry: HKLM\\...\\Services\\SysMain\\Start = 2 (Automatic)"
                    : "Registry: HKLM\\...\\Services\\SysMain\\Start = 4 (Disabled)",
                enable
                    ? Get("ram.op.sysmain_auto")
                    : Get("ram.op.sysmain_stop"),
                enable
                    ? Get("ram.op.sysmain_preloads")
                    : Get("ram.op.sysmain_ssd")
            };

            if (!PreviewDialog.ShowPreview(this, $"{action} SysMain", changes))
                return;

            await ExecuteWithSafetyAsync($"{action} SysMain", async () =>
            {
                var backups = new List<string>();

                // Backup registry key first
                var rb = _registry.SetValueWithBackup(
                    @"HKLM\SYSTEM\CurrentControlSet\Services\SysMain",
                    "Start", enable ? 2 : 4, RegistryValueKind.DWord);
                if (rb.Success) backups.Add(rb.Data!);

                // Actually start/stop the service
                if (enable)
                    await MemoryManager.EnableSysMainAsync();
                else
                    await MemoryManager.DisableSysMainAsync();

                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("ram.op.revert_sysmain", enable ? Get("word.disabled") : Get("word.enabled")),
                    BackupFiles = backups,
                    UndoDelegate = async () =>
                    {
                        if (enable)
                            await MemoryManager.DisableSysMainAsync();
                        else
                            await MemoryManager.EnableSysMainAsync();
                        return Result<bool>.Ok(true);
                    }
                });
                UpdateUndoButton();

                CheckSysMainStatus(statusLabel);
                SetStatus(Get("ram.op.sysmain_set", enable ? Get("word.enabled") : Get("word.disabled")), false);
            });
        }

        private void CheckMemoryCompressionStatus(Label label)
        {
            try
            {
                var result = MemoryManager.GetMemoryCompressionEnabled();
                if (result.Success)
                {
                    bool enabled = result.Data;
                    label.Text = enabled
                        ? Get("ram.comp_enabled")
                        : Get("ram.comp_disabled");
                    label.ForeColor = enabled ? Color.Green : Color.DarkOrange;
                }
                else
                {
                    label.Text = Get("common.unable_determine");
                    label.ForeColor = Color.Gray;
                }
            }
            catch
            {
                label.Text = Get("common.unable_determine");
                label.ForeColor = Color.Gray;
            }
        }

        private async Task ToggleMemoryCompressionAsync(bool enable, Label statusLabel)
        {
            int value = enable ? 0 : 1; // 0 = compression ON, 1 = compression OFF
            string action = enable ? Get("common.enable") : Get("common.disable");

            var changes = new List<string>
            {
                $"Registry: HKLM\\...\\Memory Management\\DisableCompression = {value}",
                enable
                    ? Get("ram.op.comp_enable_desc")
                    : Get("ram.op.comp_disable_desc"),
                Get("ram.op.comp_restart")
            };

            if (!PreviewDialog.ShowPreview(this, $"{action} Memory Compression", changes,
                Get("ram.op.comp_restart_warning")))
                return;

            await ExecuteWithSafetyAsync($"{action} Memory Compression", async () =>
            {
                var backups = new List<string>();

                var r1 = _registry.SetValueWithBackup(
                    @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                    "DisableCompression", value, RegistryValueKind.DWord);
                if (r1.Success) backups.Add(r1.Data!);

                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("ram.op.revert_comp", enable ? Get("word.disabled") : Get("word.enabled")),
                    BackupFiles = backups
                });
                UpdateUndoButton();

                CheckMemoryCompressionStatus(statusLabel);
                SetStatus(Get("ram.op.comp_set", enable ? Get("word.enabled") : Get("word.disabled")), false);
                await Task.CompletedTask;
            });
        }

        #endregion

        #region Safety & Undo

        private async Task ExecuteWithSafetyAsync(string operationName, Func<Task> action)
        {
            SetBusy(true, Get("safety.applying", operationName));
            try
            {
                // Create restore point if enabled
                if (_autoRestorePoint)
                {
                    SetStatus(Get("safety.creating_rp"), false);
                    var rpResult = _safety.CreateRestorePoint(operationName);
                    if (!rpResult.Success)
                    {
                        Logger.Instance.Warn("ExecuteWithSafety", operationName,
                            $"Restore point failed: {rpResult.ErrorMessage}");
                    }
                }

                await action();
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("ExecuteWithSafety", operationName, ex);
                SetStatus($"Error: {ex.Message}", true);
                MessageBox.Show(this,
                    Get("safety.error_msg", operationName, ex.Message),
                    Get("safety.error_title"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void UndoButton_Click(object? sender, EventArgs e)
        {
            if (_safety.UndoCount == 0)
            {
                MessageBox.Show(this, Get("safety.no_undo"), Get("dialog.kwin"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var confirm = MessageBox.Show(this,
                Get("safety.undo_confirm", _safety.LastUndoDescription ?? ""),
                Get("safety.undo_title"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            SetBusy(true, Get("safety.undoing"));
            try
            {
                var result = await _safety.UndoLastAction();
                SetStatus(result.Success
                    ? Get("safety.undo_ok")
                    : Get("safety.undo_fail", result.ErrorMessage ?? ""), !result.Success);
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

            if (_safety.UndoCount > 0)
            {
                var tip = new ToolTip();
                tip.SetToolTip(_undoButton, _safety.LastUndoDescription);
            }
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
            _statusLabel.ForeColor = isError ? Color.Red : (_isDarkMode ? DarkText : Color.Gray);
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

            // Disable/enable tab control during operations
            _tabControl.Enabled = !busy;
        }

        private void SettingsButton_Click(object? sender, EventArgs e)
        {
            using var settingsForm = new SettingsForm(_isDarkMode);
            if (settingsForm.ShowDialog(this) == DialogResult.OK)
            {
                _isDarkMode = settingsForm.DarkModeEnabled;
                _autoRestorePoint = settingsForm.AutoRestorePoint;

                if (settingsForm.AutoCleanBackups)
                {
                    _safety.CleanOldBackups(settingsForm.BackupRetentionDays);
                    Logger.Instance.CleanOldLogs(settingsForm.BackupRetentionDays);
                }

                ApplyTheme();
            }
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

        private void ApplyTheme()
        {
            Color bg = _isDarkMode ? DarkBg : LightBg;
            Color surface = _isDarkMode ? DarkSurface : LightSurface;
            Color fg = _isDarkMode ? DarkText : LightText;

            BackColor = bg;
            ForeColor = fg;

            ApplyThemeRecursive(this, bg, surface, fg);
        }

        private void ApplyThemeRecursive(Control parent, Color bg, Color surface, Color fg)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is TabControl tc)
                {
                    foreach (TabPage tp in tc.TabPages)
                    {
                        tp.BackColor = bg;
                        tp.ForeColor = fg;
                        ApplyThemeRecursive(tp, bg, surface, fg);
                    }
                }
                else if (c is GroupBox gb)
                {
                    gb.ForeColor = fg;
                    gb.BackColor = bg;
                    ApplyThemeRecursive(gb, bg, surface, fg);
                }
                else if (c is Button btn)
                {
                    // Don't override styled buttons (accent colored)
                    if (btn.BackColor == AccentBlue ||
                        btn.BackColor == Color.FromArgb(200, 60, 60) ||
                        btn.BackColor == Color.FromArgb(0, 150, 80) ||
                        btn.BackColor == Color.FromArgb(0, 100, 180))
                        continue;

                    btn.BackColor = surface;
                    btn.ForeColor = fg;
                }
                else if (c is TextBox || c is ComboBox || c is CheckedListBox || c is ListView)
                {
                    c.BackColor = surface;
                    c.ForeColor = fg;
                }
                else if (c is Label lbl)
                {
                    // Don't override specifically colored labels
                    if (lbl.ForeColor == Color.Green || lbl.ForeColor == Color.DarkOrange ||
                        lbl.ForeColor == Color.Red || lbl.ForeColor == Color.Gray ||
                        lbl.ForeColor == Color.White || lbl.ForeColor == Color.FromArgb(200, 220, 255))
                        continue;

                    lbl.ForeColor = fg;
                }
                else if (c is Panel panel && panel.BackColor != AccentBlue)
                {
                    panel.BackColor = bg;
                    panel.ForeColor = fg;
                    ApplyThemeRecursive(panel, bg, surface, fg);
                }
                else if (c is CheckBox cb)
                {
                    cb.ForeColor = fg;
                }

                if (c is TableLayoutPanel tlp)
                {
                    tlp.BackColor = bg;
                    ApplyThemeRecursive(tlp, bg, surface, fg);
                }
            }
        }

        private static bool IsFontInstalled(string fontName)
        {
            using var testFont = new Font(fontName, 10F, FontStyle.Regular, GraphicsUnit.Point);
            return testFont.Name.Equals(fontName, StringComparison.OrdinalIgnoreCase);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _ramTimer?.Stop();
            _ramTimer?.Dispose();
            Logger.Instance.Info("Application", "MainForm", "closing");
            Logger.Instance.Dispose();
            base.OnFormClosing(e);
        }

        #endregion

        #region Helper Classes

        private class PowerPlanItem
        {
            public string Guid { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public bool Active { get; set; }
            public override string ToString() => Active ? $"{Name} (Active)" : Name;
        }

        private class StartupItem
        {
            public string Name { get; set; } = string.Empty;
            public string Command { get; set; } = string.Empty;
            public string Source { get; set; } = string.Empty;
            public override string ToString() => $"{Name} — {Command}";
        }

        #endregion
    }
}
