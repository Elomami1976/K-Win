using Microsoft.Win32;
using KWin.Utils;
using static KWin.Utils.Loc;

namespace KWin.UI
{
    /// <summary>
    /// Settings form for K-win configuration: theme toggle, default preferences,
    /// about information, and backup management.
    /// </summary>
    public class SettingsForm : Form
    {
        private readonly CheckBox _darkModeCheck;
        private readonly CheckBox _createRestorePointCheck;
        private readonly CheckBox _autoCleanBackupsCheck;
        private readonly NumericUpDown _backupDaysNumeric;
        private readonly Button _openLogFolderBtn;
        private readonly Button _openBackupFolderBtn;
        private readonly Button _saveButton;
        private readonly Button _cancelButton;
        private readonly Label _versionLabel;

        /// <summary>Whether the user wants dark mode enabled.</summary>
        public bool DarkModeEnabled { get; private set; }

        /// <summary>Whether to auto-create restore points before changes.</summary>
        public bool AutoRestorePoint { get; private set; } = true;

        /// <summary>Whether to auto-clean old backups.</summary>
        public bool AutoCleanBackups { get; private set; } = true;

        /// <summary>Days to keep backup files.</summary>
        public int BackupRetentionDays { get; private set; } = 30;

        public SettingsForm(bool currentDarkMode)
        {
            Text = Get("settings.title");
            Size = new Size(450, 480);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9.5F);
            DarkModeEnabled = currentDarkMode;

            // RTL support
            if (Loc.IsRTL) {
                RightToLeft = RightToLeft.Yes;
                RightToLeftLayout = true;
            }

            // Theme section
            var themeGroup = new GroupBox
            {
                Text = Get("settings.appearance"),
                Location = new Point(15, 15),
                Size = new Size(400, 60)
            };

            _darkModeCheck = new CheckBox
            {
                Text = Get("settings.dark_mode"),
                Location = new Point(15, 25),
                Size = new Size(370, 24),
                Checked = currentDarkMode
            };
            themeGroup.Controls.Add(_darkModeCheck);

            // Safety section
            var safetyGroup = new GroupBox
            {
                Text = Get("settings.safety"),
                Location = new Point(15, 85),
                Size = new Size(400, 100)
            };

            _createRestorePointCheck = new CheckBox
            {
                Text = Get("settings.create_rp"),
                Location = new Point(15, 25),
                Size = new Size(370, 24),
                Checked = true
            };

            _autoCleanBackupsCheck = new CheckBox
            {
                Text = Get("settings.auto_clean"),
                Location = new Point(15, 55),
                Size = new Size(250, 24),
                Checked = true
            };

            _backupDaysNumeric = new NumericUpDown
            {
                Location = new Point(270, 55),
                Size = new Size(60, 24),
                Minimum = 7,
                Maximum = 365,
                Value = 30
            };

            var daysLabel = new Label
            {
                Text = Get("settings.days"),
                Location = new Point(335, 58),
                Size = new Size(40, 20)
            };

            safetyGroup.Controls.Add(_createRestorePointCheck);
            safetyGroup.Controls.Add(_autoCleanBackupsCheck);
            safetyGroup.Controls.Add(_backupDaysNumeric);
            safetyGroup.Controls.Add(daysLabel);

            // Folders section
            var foldersGroup = new GroupBox
            {
                Text = Get("settings.data_locations"),
                Location = new Point(15, 195),
                Size = new Size(400, 75)
            };

            _openLogFolderBtn = new Button
            {
                Text = Get("settings.open_log"),
                Location = new Point(15, 30),
                Size = new Size(175, 30),
                FlatStyle = FlatStyle.Flat
            };
            _openLogFolderBtn.Click += (s, e) =>
            {
                string logDir = Logger.Instance.LogDirectory;
                if (Directory.Exists(logDir))
                    System.Diagnostics.Process.Start("explorer.exe", logDir);
            };

            _openBackupFolderBtn = new Button
            {
                Text = Get("settings.open_backup"),
                Location = new Point(205, 30),
                Size = new Size(175, 30),
                FlatStyle = FlatStyle.Flat
            };
            _openBackupFolderBtn.Click += (s, e) =>
            {
                string backupDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "K-win", "backups");
                if (Directory.Exists(backupDir))
                    System.Diagnostics.Process.Start("explorer.exe", backupDir);
            };

            foldersGroup.Controls.Add(_openLogFolderBtn);
            foldersGroup.Controls.Add(_openBackupFolderBtn);

            // Language section
            var langGroup = new GroupBox
            {
                Text = Get("settings.language"),
                Location = new Point(15, 280),
                Size = new Size(400, 60)
            };

            var changeLangBtn = new Button
            {
                Text = Get("settings.change_language"),
                Location = new Point(15, 22),
                Size = new Size(175, 30),
                FlatStyle = FlatStyle.Flat
            };
            changeLangBtn.Click += (s, e) =>
            {
                var picked = LanguagePickerDialog.ShowPicker();
                if (picked != null)
                {
                    Loc.SetLanguage(picked);
                    Loc.SavePreference();
                    MessageBox.Show(this,
                        "Language changed. Please restart K-win for full effect.",
                        "K-win", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };

            var currentLangLabel = new Label
            {
                Text = Loc.CurrentLanguage.ToUpper(),
                Location = new Point(200, 28),
                Size = new Size(100, 20),
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            langGroup.Controls.Add(changeLangBtn);
            langGroup.Controls.Add(currentLangLabel);

            // Version / About
            _versionLabel = new Label
            {
                Text = Get("settings.version_info", WindowsVersionChecker.GetVersionString()),
                Location = new Point(15, 350),
                Size = new Size(400, 40),
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 8.5F)
            };

            // Buttons
            _saveButton = new Button
            {
                Text = Get("common.save"),
                Location = new Point(230, 400),
                Size = new Size(85, 32),
                BackColor = Color.FromArgb(0, 120, 212),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };
            _saveButton.FlatAppearance.BorderSize = 0;
            _saveButton.Click += (s, e) =>
            {
                DarkModeEnabled = _darkModeCheck.Checked;
                AutoRestorePoint = _createRestorePointCheck.Checked;
                AutoCleanBackups = _autoCleanBackupsCheck.Checked;
                BackupRetentionDays = (int)_backupDaysNumeric.Value;
                DialogResult = DialogResult.OK;
                Close();
            };

            _cancelButton = new Button
            {
                Text = Get("common.cancel"),
                Location = new Point(325, 400),
                Size = new Size(85, 32),
                FlatStyle = FlatStyle.Flat
            };
            _cancelButton.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            Controls.Add(themeGroup);
            Controls.Add(safetyGroup);
            Controls.Add(foldersGroup);
            Controls.Add(langGroup);
            Controls.Add(_versionLabel);
            Controls.Add(_saveButton);
            Controls.Add(_cancelButton);

            AcceptButton = _saveButton;
            CancelButton = _cancelButton;
        }

        /// <summary>
        /// Detects whether Windows is currently in dark mode.
        /// </summary>
        public static bool IsWindowsDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false);
                var val = key?.GetValue("AppsUseLightTheme");
                return val is int i && i == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
