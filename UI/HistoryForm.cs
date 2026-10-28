using System.Globalization;
using KWin.Controls;
using KWin.Models;
using KWin.Services;
using KWin.Themes;
using KWin.Utils;
using static KWin.Utils.Loc;

namespace KWin.UI
{
    /// <summary>Read-only list of the saved operation history (history.json).</summary>
    public class HistoryForm : Form
    {
        public HistoryForm(OperationHistoryService history)
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = Get("hist.title");
            Size = new Size(980, 560);
            MinimumSize = new Size(700, 400);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            Font = UiFonts.Regular(9.5f);
            if (Loc.IsRTL) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }

            var p = ThemeManager.Current;
            BackColor = p.WindowBackground;
            ForeColor = p.Text;

            var list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BorderStyle = BorderStyle.None,
                BackColor = p.Card,
                ForeColor = p.Text,
                ShowItemToolTips = true
            };
            list.Columns.Add(Get("hist.col_time"), 150);
            list.Columns.Add(Get("hist.col_operation"), 260);
            list.Columns.Add(Get("hist.col_result"), 100);
            list.Columns.Add(Get("hist.col_undo"), 110);
            list.Columns.Add(Get("hist.col_restore_point"), 140);
            list.Columns.Add(Get("hist.col_details"), 400);

            CultureInfo culture;
            try { culture = CultureInfo.GetCultureInfo(Loc.CurrentLanguage); } catch { culture = CultureInfo.CurrentCulture; }

            var records = history.GetRecent(OperationHistoryService.MaxEntries);
            foreach (var r in records)
            {
                var item = new ListViewItem((r.CompletedUtc ?? r.StartedUtc).ToLocalTime().ToString("g", culture));
                item.SubItems.Add(r.Name);
                item.SubItems.Add(StatusText(r.Status));
                item.SubItems.Add(ReversibilityText(r.Reversibility));
                item.SubItems.Add(RestorePointText(r.RestorePoint));
                string details = string.Join(" · ", new[]
                {
                    r.RegistryChanges.Count > 0 ? Get("hist.registry_changes", r.RegistryChanges.Count) : null,
                    r.Details,
                    r.Error
                }.Where(s => !string.IsNullOrWhiteSpace(s)));
                item.SubItems.Add(details);
                item.ToolTipText = details;
                item.ForeColor = r.Status switch
                {
                    OperationStatus.Failed => p.Error,
                    OperationStatus.Cancelled or OperationStatus.Skipped => p.TextMuted,
                    _ => p.Text
                };
                list.Items.Add(item);
            }

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(12) };
            var summary = new ThemedLabel
            {
                Text = records.Count == 0 ? Get("dash.no_actions") : Get("hist.summary", records.Count, history.FilePath),
                Role = TextRole.Muted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var close = new AccentButton { Text = Get("hist.close"), Dock = DockStyle.Right, Width = 120 };
            close.Click += (s, e) => Close();
            bottom.Controls.Add(summary);
            bottom.Controls.Add(close);

            Controls.Add(list);
            Controls.Add(bottom);
            ThemeManager.ApplyTree(this);
            AcceptButton = null;
            CancelButton = null;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ThemeManager.ApplyTitleBar(this);
        }

        public static string StatusText(OperationStatus s) => s switch
        {
            OperationStatus.Completed => Get("hist.status_completed"),
            OperationStatus.Failed => Get("hist.status_failed"),
            OperationStatus.Cancelled => Get("hist.status_cancelled"),
            OperationStatus.Skipped => Get("hist.status_skipped"),
            OperationStatus.RestartRequired => Get("hist.status_restart"),
            _ => s.ToString()
        };

        private static string ReversibilityText(Reversibility r) => r switch
        {
            Reversibility.Full => Get("hist.rev_full"),
            Reversibility.Partial => Get("hist.rev_partial"),
            _ => Get("hist.rev_none")
        };

        private static string RestorePointText(RestorePointStatus s) => s switch
        {
            RestorePointStatus.Created => Get("hist.rp_created"),
            RestorePointStatus.SkippedRecentExists => Get("hist.rp_recent"),
            RestorePointStatus.SkippedNotAdmin => Get("hist.rp_not_admin"),
            RestorePointStatus.Failed => Get("hist.rp_failed"),
            _ => "—"
        };
    }
}
