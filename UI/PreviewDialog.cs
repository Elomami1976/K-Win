using static KWin.Utils.Loc;

namespace KWin.UI
{
    /// <summary>
    /// Modal dialog that shows a detailed preview of all changes that will be made
    /// before applying an optimization. The user must confirm to proceed.
    /// </summary>
    public class PreviewDialog : Form
    {
        private readonly ListView _changeList;
        private readonly Label _summaryLabel;
        private readonly Label _warningLabel;
        private readonly Button _applyButton;
        private readonly Button _cancelButton;
        private readonly Panel _buttonPanel;

        /// <summary>Gets whether the user confirmed the changes.</summary>
        public bool Confirmed { get; private set; }

        public PreviewDialog(string title, List<string> changes, string? warningText = null)
        {
            // Form setup
            Text = Get("preview.title", title);
            Size = new Size(600, 450);
            MinimumSize = new Size(500, 350);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9.5F);

            // RTL support
            if (KWin.Utils.Loc.IsRTL){
                RightToLeft = RightToLeft.Yes;
                RightToLeftLayout = true;
            }

            // Summary label
            _summaryLabel = new Label
            {
                Text = Get("preview.summary", changes.Count),
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 5, 10, 0),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };

            // Warning label (if applicable)
            _warningLabel = new Label
            {
                Text = warningText ?? string.Empty,
                Dock = DockStyle.Top,
                Height = warningText != null ? 40 : 0,
                Visible = warningText != null,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 10, 0),
                ForeColor = Color.DarkOrange,
                Font = new Font("Segoe UI", 9F, FontStyle.Italic)
            };

            // Change list view
            _changeList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Font = new Font("Segoe UI", 9F)
            };
            _changeList.Columns.Add(Get("preview.col_num"), 40);
            _changeList.Columns.Add(Get("preview.col_desc"), 500);

            for (int i = 0; i < changes.Count; i++)
            {
                var item = new ListViewItem((i + 1).ToString());
                item.SubItems.Add(changes[i]);
                _changeList.Items.Add(item);
            }

            // Button panel
            _buttonPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                Padding = new Padding(10)
            };

            _applyButton = new Button
            {
                Text = Get("preview.apply"),
                Size = new Size(130, 32),
                BackColor = Color.FromArgb(0, 120, 212),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };
            _applyButton.FlatAppearance.BorderSize = 0;
            _applyButton.Click += (s, e) => { Confirmed = true; DialogResult = DialogResult.OK; Close(); };

            _cancelButton = new Button
            {
                Text = Get("common.cancel"),
                Size = new Size(90, 32),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9.5F)
            };
            _cancelButton.Click += (s, e) => { Confirmed = false; DialogResult = DialogResult.Cancel; Close(); };

            // Layout buttons
            _applyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _applyButton.Location = new Point(_buttonPanel.Width - 240, 9);
            _cancelButton.Location = new Point(_buttonPanel.Width - 100, 9);

            _buttonPanel.Controls.Add(_applyButton);
            _buttonPanel.Controls.Add(_cancelButton);

            // Add controls (order matters for DockStyle)
            Controls.Add(_changeList);
            Controls.Add(_warningLabel);
            Controls.Add(_summaryLabel);
            Controls.Add(_buttonPanel);

            // Handle resize for button positioning
            _buttonPanel.Resize += (s, e) =>
            {
                _applyButton.Location = new Point(_buttonPanel.Width - 240, 9);
                _cancelButton.Location = new Point(_buttonPanel.Width - 100, 9);
            };

            AcceptButton = _applyButton;
            CancelButton = _cancelButton;
        }

        /// <summary>
        /// Shows a preview dialog and returns whether the user confirmed.
        /// </summary>
        public static bool ShowPreview(IWin32Window owner, string title,
            List<string> changes, string? warningText = null)
        {
            using var dialog = new PreviewDialog(title, changes, warningText);
            return dialog.ShowDialog(owner) == DialogResult.OK && dialog.Confirmed;
        }
    }
}
