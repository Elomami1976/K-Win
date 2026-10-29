using KWin.Controls;
using KWin.Models;
using KWin.Services;
using KWin.Themes;
using KWin.Utils;
using static KWin.Utils.Loc;

namespace KWin.UI
{
    /// <summary>
    /// Optimize Now, step "choose": shows the recommendations (grouped by risk, with current and
    /// recommended state) and the details of the selected one. Nothing is changed here; the caller
    /// shows the full preview and asks for confirmation afterwards.
    /// Also used for the final summary (per-item results).
    /// </summary>
    public sealed class OptimizeDialog : Form
    {
        private readonly ThemedListView _list = new() { Dock = DockStyle.Fill, CheckBoxes = true };
        private readonly ThemedLabel _details;
        private readonly Func<SettingDefinition, string> _undoText;
        private bool _filling;

        public IReadOnlyList<Recommendation> Selected =>
            _list.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (Recommendation)i.Tag!).ToList();

        private OptimizeDialog(string title, Func<SettingDefinition, string> undoText)
        {
            _undoText = undoText;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = title;
            Size = new Size(1000, 640);
            MinimumSize = new Size(760, 480);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            Font = UiFonts.Regular(9.5f);
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
            if (Loc.IsRTL) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
            _details = new ThemedLabel { Dock = DockStyle.Fill, AutoEllipsis = false, Font = UiFonts.Regular(10f), Padding = new Padding(12) };
        }

        /// <summary>Choose recommendations. Returns the dialog; read <see cref="Selected"/> when OK.</summary>
        public static OptimizeDialog ForSelection(AnalysisResult analysis, Func<SettingDefinition, string> undoText)
        {
            var d = new OptimizeDialog(Get("opt.title"), undoText);
            d.BuildSelection(analysis);
            return d;
        }

        /// <summary>Per-item results after applying.</summary>
        public static OptimizeDialog ForSummary(IReadOnlyList<ItemResult> results, string restorePointText, bool restartNeeded, Func<SettingDefinition, string> undoText)
        {
            var d = new OptimizeDialog(Get("opt.summary_title"), undoText);
            d.BuildSummary(results, restorePointText, restartNeeded);
            return d;
        }

        private TableLayoutPanel Frame(string intro, out FlowLayoutPanel buttons)
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(16) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

            var introLabel = new ThemedLabel
            {
                Text = intro,
                AutoSize = true,
                AutoEllipsis = false,
                MaximumSize = new Size(940, 0),
                Font = UiFonts.Regular(10f),
                Margin = new Padding(0, 0, 0, 10)
            };
            root.Controls.Add(introLabel, 0, 0);
            Resize += (s, e) => introLabel.MaximumSize = new Size(Math.Max(300, ClientSize.Width - 40), 0);

            root.Controls.Add(_list, 0, 1);

            var detailsCard = new RoundedPanel { Kind = SurfaceKind.Card, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0), AutoScroll = true };
            detailsCard.Controls.Add(_details);
            root.Controls.Add(detailsCard, 0, 2);

            buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
            root.Controls.Add(buttons, 0, 3);
            return root;
        }

        private AccentButton Button(string text, bool primary, DialogResult result)
        {
            var b = new AccentButton { Text = text, Secondary = !primary, FontSize = 10f, Height = 36, Margin = new Padding(8, 0, 0, 0) };
            using (var g = CreateGraphics())
                b.Width = TextRenderer.MeasureText(g, text, UiFonts.Semibold(10f)).Width + Draw.S(this, 40);
            b.Click += (s, e) => { DialogResult = result; Close(); };
            return b;
        }

        private static string StateText(SettingDefinition d, SettingState s) => s switch
        {
            SettingState.On => Get(d.TextKey("on")),
            SettingState.Off => Get(d.TextKey("off")),
            SettingState.Custom => Get("card.state_custom"),
            _ => Get("card.state_unknown")
        };

        public static string RiskText(RiskLevel r) => r switch
        {
            RiskLevel.Low => Get("opt.risk_low"),
            RiskLevel.Medium => Get("opt.risk_medium"),
            _ => Get("opt.risk_high")
        };

        private void BuildSelection(AnalysisResult a)
        {
            var intro = new List<string>
            {
                a.Recommendations.Count == 0 ? Get("opt.none_needed") : Get("opt.intro", a.Recommendations.Count),
                Get("opt.intro_safety")
            };
            if (a.AlreadyRecommended.Count > 0) intro.Add(Get("opt.already", a.AlreadyRecommended.Count));
            if (a.Preferences.Count > 0) intro.Add(Get("opt.preferences", a.Preferences.Count));
            foreach (var (s, reason) in a.Skipped)
                intro.Add("• " + Get("opt.skipped", Get(s.TextKey("title")), Get(reason switch
                {
                    SkipReason.NotApplicable => "opt.skip_notapplicable",
                    SkipReason.LockedByOrganization => "opt.skip_lockedbyorganization",
                    SkipReason.LockedByLocalPolicy => "opt.skip_lockedbylocalpolicy",
                    _ => "opt.skip_stateunknown"
                })));

            var root = Frame(string.Join(Environment.NewLine, intro), out var buttons);

            _list.Columns.Add(Get("opt.col_risk"), 110);
            _list.Columns.Add(Get("opt.col_setting"), 230);
            _list.Columns.Add(Get("opt.col_current"), 200);
            _list.Columns.Add(Get("opt.col_recommended"), 170);
            _list.Columns.Add(Get("card.requires"), 200);
            var p = ThemeManager.Current;
            _filling = true;
            foreach (var r in a.Recommendations)
            {
                var item = new ListViewItem(RiskText(r.Risk)) { Tag = r, Checked = r.Selected };
                item.SubItems.Add(Get(r.Setting.TextKey("title")));
                item.SubItems.Add(StateText(r.Setting, r.Current));
                item.SubItems.Add(Get(r.Setting.TextKey(r.TargetOn ? "on" : "off")));
                item.SubItems.Add(r.Setting.RequiresAdmin ? Get("card.requires_admin") : Get("card.requires_user"));
                item.UseItemStyleForSubItems = false;
                item.SubItems[0].ForeColor = r.Risk == RiskLevel.Low ? p.Success : r.Risk == RiskLevel.Medium ? p.Warning : p.Error;
                for (int i = 1; i < item.SubItems.Count; i++) item.SubItems[i].ForeColor = p.Text;
                _list.Items.Add(item);
            }
            _filling = false;

            var apply = Button(Get("opt.review_apply"), true, DialogResult.OK);
            var cancel = Button(Get("opt.cancel"), false, DialogResult.Cancel);
            buttons.Controls.Add(apply);
            buttons.Controls.Add(cancel);
            AcceptButton = null;
            CancelButton = null;

            void UpdateApply() => apply.Enabled = _list.Items.Cast<ListViewItem>().Any(i => i.Checked);
            _list.ItemChecked += (s, e) =>
            {
                if (_filling) return;
                if (e.Item.Tag is Recommendation rec) rec.Selected = e.Item.Checked;
                UpdateApply();
            };
            _list.SelectedIndexChanged += (s, e) => ShowDetails();
            UpdateApply();

            Controls.Add(root);
            if (_list.Items.Count > 0) _list.Items[0].Selected = true;
            ShowDetails();
            ThemeManager.ApplyTree(this);
            BackColor = p.WindowBackground;
        }

        private void ShowDetails()
        {
            if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not Recommendation r)
            {
                _details.Text = Get("opt.select_hint");
                return;
            }
            var d = r.Setting;
            var lines = new List<string>
            {
                Get(d.TextKey("title")) + " — " + RiskText(r.Risk),
                Get(d.TextKey("desc")),
                "",
                $"{Get("opt.col_current")}: {StateText(d, r.Current)}   →   {Get("opt.col_recommended")}: {Get(d.TextKey(r.TargetOn ? "on" : "off"))}",
                $"{Get("card.benefit")}: {Get(d.TextKey("benefit"))}",
                $"{Get("card.side_effects")}: {Get(d.TextKey("side"))}",
                $"{Get("card.undo")}: {_undoText(d)}"
            };
            if (d.RestartRequired) lines.Add(Get("card.restart_needed"));
            else if (d.SignOutRequired) lines.Add(Get("card.signout_needed"));
            if (r.ChangedOutsideKWin) lines.Add(Get("opt.changed_outside"));
            _details.Text = string.Join(Environment.NewLine, lines);
        }

        private void BuildSummary(IReadOnlyList<ItemResult> results, string restorePointText, bool restartNeeded)
        {
            int ok = results.Count(r => r.Outcome == ItemOutcome.Applied);
            int failed = results.Count(r => r.Outcome == ItemOutcome.Failed);
            var intro = new List<string> { Get("opt.summary_intro", ok, failed, results.Count), restorePointText };
            if (restartNeeded) intro.Add(Get("opt.summary_restart"));
            if (ok > 0) intro.Add(Get("opt.summary_undo"));
            var root = Frame(string.Join(Environment.NewLine, intro), out var buttons);
            _list.CheckBoxes = false;

            _list.Columns.Add(Get("opt.col_result"), 150);
            _list.Columns.Add(Get("opt.col_setting"), 250);
            _list.Columns.Add(Get("opt.col_recommended"), 180);
            _list.Columns.Add(Get("opt.col_details"), 360);
            var p = ThemeManager.Current;
            foreach (var r in results)
            {
                string outcome = r.Outcome switch
                {
                    ItemOutcome.Applied => Get("opt.applied"),
                    ItemOutcome.Failed => Get("opt.failed"),
                    _ => Get("opt.not_run")
                };
                var item = new ListViewItem(outcome) { Tag = r };
                item.SubItems.Add(Get(r.Recommendation.Setting.TextKey("title")));
                item.SubItems.Add(Get(r.Recommendation.Setting.TextKey(r.Recommendation.TargetOn ? "on" : "off")));
                item.SubItems.Add(r.Error ?? r.Warning ?? "");
                item.UseItemStyleForSubItems = false;
                item.SubItems[0].ForeColor = r.Outcome == ItemOutcome.Applied ? (r.Warning == null ? p.Success : p.Warning) : p.Error;
                for (int i = 1; i < item.SubItems.Count; i++) item.SubItems[i].ForeColor = p.Text;
                item.ToolTipText = r.Error ?? r.Warning ?? "";
                _list.Items.Add(item);
            }
            _list.ShowItemToolTips = true;
            _list.SelectedIndexChanged += (s, e) =>
            {
                if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not ItemResult r) return;
                _details.Text = string.Join(Environment.NewLine, new[]
                {
                    Get(r.Recommendation.Setting.TextKey("title")),
                    r.Error != null ? $"{Get("opt.failed")}: {r.Error}" : Get("opt.applied_verified"),
                    r.Warning != null ? $"{Get("opt.warning")}: {r.Warning}" : "",
                    $"{Get("card.undo")}: {_undoText(r.Recommendation.Setting)}"
                }.Where(x => x.Length > 0));
            };
            _details.Text = Get("opt.summary_hint");
            buttons.Controls.Add(Button(Get("hist.close"), true, DialogResult.OK));
            Controls.Add(root);
            ThemeManager.ApplyTree(this);
            BackColor = p.WindowBackground;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ThemeManager.ApplyTitleBar(this);
        }
    }
}
