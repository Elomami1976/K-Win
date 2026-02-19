using KWin.Utils;

namespace KWin.UI
{
    /// <summary>
    /// First-run language picker dialog. Shows available languages with native names.
    /// Displayed on first launch when no language preference is saved.
    /// </summary>
    public class LanguagePickerDialog : Form
    {
        private string _selectedLang = "en";

        /// <summary>Gets the language code chosen by the user.</summary>
        public string SelectedLanguage => _selectedLang;

        public LanguagePickerDialog()
        {
            Text = "K-win — Choose Language";
            Size = new Size(480, 420);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            Font = new Font("Segoe UI", 10F);
            BackColor = Color.FromArgb(32, 32, 32);
            ForeColor = Color.White;

            // Header icon/title
            var titleLabel = new Label
            {
                Text = "Welcome to K-win",
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 60,
                Padding = new Padding(0, 10, 0, 0)
            };

            var subtitleLabel = new Label
            {
                Text = "Choose your language:",
                Font = new Font("Segoe UI", 11F),
                ForeColor = Color.FromArgb(180, 180, 180),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 30
            };

            // Language buttons panel
            var langPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(80, 10, 80, 10)
            };

            Button? selectedButton = null;
            var accentBlue = Color.FromArgb(0, 120, 212);

            foreach (var (code, native, english) in Loc.AvailableLanguages)
            {
                var btn = new Button
                {
                    Text = code == "en" ? native : $"{native}  —  {english}",
                    Tag = code,
                    Size = new Size(300, 42),
                    Margin = new Padding(0, 4, 0, 4),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = code == "en" ? accentBlue : Color.FromArgb(55, 55, 55),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 11F, code == "en" ? FontStyle.Bold : FontStyle.Regular),
                    Cursor = Cursors.Hand,
                    TextAlign = ContentAlignment.MiddleCenter
                };
                btn.FlatAppearance.BorderColor = code == "en" ? accentBlue : Color.FromArgb(80, 80, 80);
                btn.FlatAppearance.BorderSize = 2;
                btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 100, 180);

                if (code == "en") selectedButton = btn;

                btn.Click += (s, e) =>
                {
                    _selectedLang = code;
                    // Reset all buttons
                    foreach (Control c in langPanel.Controls)
                    {
                        if (c is Button b)
                        {
                            b.BackColor = Color.FromArgb(55, 55, 55);
                            b.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
                            b.Font = new Font("Segoe UI", 11F, FontStyle.Regular);
                        }
                    }
                    // Highlight selected
                    btn.BackColor = accentBlue;
                    btn.FlatAppearance.BorderColor = accentBlue;
                    btn.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
                    selectedButton = btn;
                };

                langPanel.Controls.Add(btn);
            }

            // Continue button
            var continuePanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                Padding = new Padding(80, 10, 80, 15)
            };

            var continueBtn = new Button
            {
                Text = "Continue →",
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 150, 80),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Height = 40
            };
            continueBtn.FlatAppearance.BorderSize = 0;
            continueBtn.Click += (s, e) =>
            {
                DialogResult = DialogResult.OK;
                Close();
            };

            continuePanel.Controls.Add(continueBtn);

            Controls.Add(langPanel);
            Controls.Add(subtitleLabel);
            Controls.Add(titleLabel);
            Controls.Add(continuePanel);

            AcceptButton = continueBtn;
        }

        /// <summary>
        /// Shows the language picker and returns the selected language code.
        /// Returns "en" if the dialog is cancelled.
        /// </summary>
        public static string ShowPicker()
        {
            using var dialog = new LanguagePickerDialog();
            if (dialog.ShowDialog() == DialogResult.OK)
                return dialog.SelectedLanguage;
            return "en";
        }
    }
}
