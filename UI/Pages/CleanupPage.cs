using KWin.Controls;
using KWin.Core;
using KWin.Services;
using KWin.Themes;
using KWin.Utils;
using static KWin.Utils.Loc;

namespace KWin.UI.Pages
{
    public sealed class CleanupActions
    {
        /// <summary>Shows the preview + confirmation, runs the cleanup and records it. Returns null when cancelled at confirmation.</summary>
        public required Func<IReadOnlyList<CleanupCategory>, IProgress<string>, CancellationToken, Task<List<CategoryResult>?>> Execute { get; init; }
        public required Func<Task> ResetStoreCache { get; init; }
    }

    /// <summary>
    /// Cleanup workflow: 1 scan, 2 review categories and measured sizes, 3 select, 4 preview and
    /// confirm, 5 clean with progress (cancellable), 6 completion report. Uses the Phase 2 safe
    /// cleanup engine. Deleting files cannot be undone; the page says so.
    /// </summary>
    public sealed class CleanupPage : UserControl, IThemedControl
    {
        private readonly CleanupActions _a;
        private readonly CleanupPlanner _planner = new();
        private readonly ThemedListView _list = new() { Dock = DockStyle.Fill, CheckBoxes = true };
        private readonly ThemedLabel _step, _total, _report;
        private readonly AccentButton _scan, _clean, _cancel;
        private readonly ProgressBar _progress = new() { Height = 8, Dock = DockStyle.Fill, Visible = false, Margin = new Padding(0, 10, 0, 10) };
        private List<CleanupCategory> _categories = new();
        private CancellationTokenSource? _cts;
        private bool _filling;
        private bool _running;

        public CleanupPage(CleanupActions a)
        {
            _a = a;
            DoubleBuffered = true;

            var root = Ui.Grid(1, 2);
            Ui.Rows(root, (SizeType.Percent, 100), (SizeType.Absolute, 92));

            var card = new RoundedPanel { Kind = SurfaceKind.Card, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8), Padding = new Padding(14) };
            var grid = Ui.Grid(1, 5);
            Ui.Rows(grid, (SizeType.Absolute, 44), (SizeType.Absolute, 28), (SizeType.Percent, 100), (SizeType.Absolute, 28), (SizeType.Absolute, 54));

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            _scan = Button(Get("cleanup.scan"), true, async () => await ScanAsync());
            _clean = Button(Get("cleanup.clean_selected"), false, async () => await CleanAsync());
            _cancel = Button(Get("cleanup.cancel"), false, () => { _cts?.Cancel(); return Task.CompletedTask; });
            _clean.Enabled = false;
            _cancel.Visible = false;
            toolbar.Controls.Add(_scan);
            toolbar.Controls.Add(_clean);
            toolbar.Controls.Add(_cancel);
            grid.Controls.Add(toolbar, 0, 0);

            _step = Ui.Text(Get("cleanup.step_start"), UiFonts.Regular(9.5f), TextRole.Muted);
            grid.Controls.Add(_step, 0, 1);

            _list.Columns.Add(Get("cleanup.col_category"), 240);
            // Wide enough for "Needs administrator" in every language (it was clipped at 110).
            int sizeWidth = Math.Max(110, TextRenderer.MeasureText(Get("cleanup.needs_admin"), _list.Font).Width + 24);
            _list.Columns.Add(Get("cleanup.col_size"), sizeWidth);
            _list.Columns.Add(Get("cleanup.col_description"), 520);
            _list.NumericColumns.Add(1);
            _list.ShowItemToolTips = true;
            _list.ItemChecked += (s, e) =>
            {
                if (_filling || e.Item.Tag is not CleanupCategory c) return;
                if (_running) { _filling = true; e.Item.Checked = c.Selected; _filling = false; return; } // locked while running
                if (c.RequiresAdmin && c.Targets.Count == 0) { _filling = true; e.Item.Checked = false; _filling = false; return; }
                c.Selected = e.Item.Checked;
                UpdateTotal();
            };
            grid.Controls.Add(_list, 0, 2);

            _total = Ui.Text("", UiFonts.Semibold(10f));
            grid.Controls.Add(_total, 0, 3);

            var bottom = Ui.Grid(2, 1);
            Ui.Columns(bottom, (SizeType.Percent, 100), (SizeType.Absolute, 220));
            _report = Ui.Text(Get("cleanup.irreversible"), UiFonts.Regular(9f), TextRole.Muted, wrap: true);
            bottom.Controls.Add(_report, 0, 0);
            bottom.Controls.Add(_progress, 1, 0);
            grid.Controls.Add(bottom, 0, 4);

            card.Controls.Add(grid);
            root.Controls.Add(card, 0, 0);

            // Other cleanup tools (1.0 features)
            var tools = new RoundedPanel { Kind = SurfaceKind.Card, Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(14, 10, 14, 10) };
            var tgrid = Ui.Grid(2, 1);
            Ui.Columns(tgrid, (SizeType.Percent, 100), (SizeType.Absolute, 240));
            var ttext = Ui.Grid(1, 2);
            ttext.Controls.Add(Ui.Text(Get("cleanup.store_title"), UiFonts.Semibold(10.5f)), 0, 0);
            ttext.Controls.Add(Ui.Text(Get("cleanup.store_desc"), UiFonts.Regular(9f), TextRole.Muted), 0, 1);
            tgrid.Controls.Add(ttext, 0, 0);
            var store = Button(Get("cleanup.store_run"), false, _a.ResetStoreCache);
            store.Anchor = AnchorStyles.Right;
            tgrid.Controls.Add(store, 1, 0);
            tools.Controls.Add(tgrid);
            root.Controls.Add(tools, 0, 1);

            Controls.Add(root);

            // Step 1 runs by itself the first time the page opens: scanning only measures.
            VisibleChanged += async (s, e) =>
            {
                if (Visible && _categories.Count == 0 && _cts == null) await ScanAsync();
            };
        }

        private AccentButton Button(string text, bool primary, Func<Task> onClick)
        {
            var b = new AccentButton { Text = text, Secondary = !primary, FontSize = 9.5f, Height = 32, Margin = new Padding(0, 4, 8, 0) };
            using (var g = CreateGraphics())
                b.Width = TextRenderer.MeasureText(g, text, UiFonts.Semibold(9.5f)).Width + Draw.S(this, 36);
            b.Click += async (s, e) => await onClick();
            return b;
        }

        /// <summary>Step 1: measure every category (nothing is deleted).</summary>
        public async Task ScanAsync()
        {
            SetRunning(true);
            _step.Text = Get("cleanup.scanning");
            _categories = CleanupPlanner.DefaultCategories(Get);
            _cts = new CancellationTokenSource();
            try
            {
                _progress.Maximum = Math.Max(1, _categories.Count);
                _progress.Value = 0;
                await _planner.ScanAsync(_categories, new Progress<int>(n => _progress.Value = Math.Min(n, _progress.Maximum)), _cts.Token);
                FillList();
                _step.Text = Get("cleanup.step_review");
            }
            catch (OperationCanceledException)
            {
                _step.Text = Get("cleanup.scan_cancelled");
            }
            finally
            {
                SetRunning(false);
                _cts.Dispose();
                _cts = null;
            }
        }

        private void FillList()
        {
            _filling = true;
            _list.BeginUpdate();
            _list.Items.Clear();
            var p = ThemeManager.Current;
            foreach (var c in _categories)
            {
                bool unavailable = c.RequiresAdmin && c.Targets.Count == 0;
                string size = unavailable ? Get("cleanup.needs_admin")
                    : c.MeasuredBytes is long b ? Loc.Ltr(CleanupManager.FormatSize(b)) : Get("dash.unavailable");
                if (c.Kind == CleanupKind.RecycleBin && c.MeasuredItems is long items)
                    size += " · " + Get("cleanup.items", items);
                var item = new ListViewItem(c.Name) { Tag = c, Checked = c.Selected && !unavailable, ToolTipText = string.Join("\n", c.Targets.Select(t => t.Path)) };
                item.SubItems.Add(new ListViewItem.ListViewSubItem(item, size) { Tag = c.MeasuredBytes ?? -1L });
                item.SubItems.Add(c.Description);
                if (unavailable) item.ForeColor = p.TextMuted;
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            _filling = false;
            UpdateTotal();
        }

        private void UpdateTotal()
        {
            var sel = _categories.Where(c => c.Selected).ToList();
            long total = sel.Sum(c => c.MeasuredBytes ?? 0);
            _total.Text = Get("cleanup.selected_total", sel.Count, Loc.Ltr(CleanupManager.FormatSize(total)));
            _clean.Enabled = sel.Count > 0 && _cts == null;
        }

        /// <summary>Steps 4-6: preview/confirm (main window), clean with progress, report.</summary>
        private async Task CleanAsync()
        {
            if (_categories.Count == 0) return;
            SetRunning(true);
            _cts = new CancellationTokenSource();
            var progress = new Progress<string>(msg => _step.Text = Get("cleanup.cleaning", msg));
            try
            {
                var results = await _a.Execute(_categories, progress, _cts.Token);
                if (results == null)
                {
                    _step.Text = Get("cleanup.step_review");
                    return;
                }
                ShowReport(results);
            }
            finally
            {
                SetRunning(false);
                _cts?.Dispose();
                _cts = null;
            }
            await ScanAsync(); // show what is left
        }

        private void ShowReport(List<CategoryResult> results)
        {
            long freed = results.Sum(r => r.Report.BytesFreed);
            int files = results.Sum(r => r.Report.FilesDeleted);
            int skipped = results.Sum(r => r.Report.FilesSkipped);
            bool cancelled = results.Any(r => r.Outcome is CategoryOutcome.Cancelled or CategoryOutcome.NotRun);
            int failed = results.Count(r => r.Outcome == CategoryOutcome.Failed);

            var lines = new List<string>
            {
                Get(cancelled ? "cleanup.report_cancelled" : "cleanup.report_done", files, Loc.Ltr(CleanupManager.FormatSize(freed)), skipped)
            };
            if (failed > 0)
                lines.Add(Get("cleanup.report_failed", string.Join(", ", results.Where(r => r.Outcome == CategoryOutcome.Failed).Select(r => r.Category.Name))));
            _report.Text = string.Join("  ", lines);
            _report.Role = failed > 0 ? TextRole.Warning : TextRole.Success;
        }

        private void SetRunning(bool running)
        {
            _scan.Enabled = !running;
            _clean.Enabled = !running && _categories.Any(c => c.Selected);
            _cancel.Visible = running;
            _progress.Visible = running;
            _progress.Style = running && _categories.Count == 0 ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            _running = running;
        }

        public void ApplyTheme(ThemePalette p)
        {
            BackColor = p.WindowBackground;
        }
    }
}
