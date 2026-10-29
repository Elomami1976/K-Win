using KWin.Controls;
using KWin.Services;
using KWin.Themes;
using KWin.Utils;
using static KWin.Utils.Loc;

namespace KWin.UI.Pages
{
    public sealed class StartupActions
    {
        /// <summary>Discovers entries (on a background thread, with publisher details).</summary>
        public required Func<Task<List<StartupEntry>>> Load { get; init; }
        /// <summary>Enables/disables entries with preview, safety workflow and undo.</summary>
        public required Func<IReadOnlyList<StartupEntry>, bool, Task> SetEnabled { get; init; }
    }

    /// <summary>
    /// Startup Manager: every startup entry from the Run keys (user, all users 64-bit and 32-bit)
    /// and the Startup folders, with publisher, source and status. Search, sort, refresh,
    /// enable and disable (via StartupApproved, never deleting entries).
    /// </summary>
    public sealed class StartupPage : UserControl, IThemedControl
    {
        private readonly StartupActions _a;
        private readonly ThemedListView _list = new() { Dock = DockStyle.Fill, CheckBoxes = true, MultiSelect = true };
        private readonly TextBox _search = new() { Width = 240, BorderStyle = BorderStyle.FixedSingle };
        private readonly ThemedLabel _summary;
        private readonly TextBox _details = DetailsBox();
        private List<StartupEntry> _all = new();
        private bool _loading;

        public StartupPage(StartupActions a)
        {
            _a = a;
            DoubleBuffered = true;

            var card = new RoundedPanel { Kind = SurfaceKind.Card, Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(14) };
            var grid = Ui.Grid(1, 5);
            Ui.Rows(grid, (SizeType.Absolute, 44), (SizeType.Absolute, 30), (SizeType.Percent, 100), (SizeType.Absolute, 28), (SizeType.Absolute, 92));

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            _search.Font = UiFonts.Regular(10f);
            _search.Margin = new Padding(0, 6, 12, 0);
            _search.PlaceholderText = Get("startup.search");
            _search.AccessibleName = Get("startup.search");
            _search.TextChanged += (s, e) => Fill();
            toolbar.Controls.Add(_search);
            toolbar.Controls.Add(Button(Get("startup.refresh"), false, async () => await LoadAsync()));
            toolbar.Controls.Add(Button(Get("startup.disable"), true, async () => await ChangeAsync(false)));
            toolbar.Controls.Add(Button(Get("startup.enable"), false, async () => await ChangeAsync(true)));
            grid.Controls.Add(toolbar, 0, 0);

            grid.Controls.Add(Ui.Text(Get("startup.explain"), UiFonts.Regular(9f), TextRole.Muted), 0, 1);

            _list.Columns.Add(Get("startup.col_name"), 200);
            _list.Columns.Add(Get("startup.col_status"), 90);
            _list.Columns.Add(Get("startup.col_publisher"), 170);
            _list.Columns.Add(Get("startup.col_source"), 170);
            _list.Columns.Add(Get("startup.col_command"), 420);
            _list.ShowItemToolTips = true;
            grid.Controls.Add(_list, 0, 2);

            _summary = Ui.Text("", UiFonts.Regular(9.5f), TextRole.Muted);
            grid.Controls.Add(_summary, 0, 3);
            grid.Controls.Add(_details, 0, 4);
            _list.SelectedIndexChanged += (s, e) => ShowDetails();

            card.Controls.Add(grid);
            Controls.Add(card);
            VisibleChanged += async (s, e) => { if (Visible && _all.Count == 0) await LoadAsync(); };
        }

        private Control Button(string text, bool primary, Func<Task> onClick)
        {
            var b = new AccentButton { Text = text, Secondary = !primary, FontSize = 9.5f, Height = 32, Margin = new Padding(0, 4, 8, 0) };
            using (var g = CreateGraphics())
                b.Width = TextRenderer.MeasureText(g, text, UiFonts.Semibold(9.5f)).Width + Draw.S(this, 36);
            b.Click += async (s, e) => await onClick();
            return b;
        }

        public async Task LoadAsync()
        {
            if (_loading) return;
            _loading = true;
            _summary.Text = Get("startup.loading");
            try
            {
                _all = await _a.Load();
                Fill();
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("StartupPage", "load", ex);
                _summary.Text = Get("failed", ex.Message);
            }
            finally
            {
                _loading = false;
            }
        }

        private void Fill()
        {
            string q = _search.Text.Trim();
            var p = ThemeManager.Current;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var e in _all.Where(e => q.Length == 0 ||
                         e.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                         (e.Publisher?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                         e.Command.Contains(q, StringComparison.CurrentCultureIgnoreCase)))
            {
                string status = e.Enabled ? Get("startup.enabled") : Get("startup.disabled");
                if (!e.TargetExists) status += " · " + Get("startup.missing");
                var item = new ListViewItem(e.Name) { Tag = e, ToolTipText = $"{e.Command}\n{e.Location}" };
                item.SubItems.Add(status);
                item.SubItems.Add(e.Publisher ?? Get("startup.unknown_publisher"));
                item.SubItems.Add(SourceText(e.Source));
                item.SubItems.Add(e.Command);
                item.ForeColor = !e.TargetExists ? p.Warning : e.Enabled ? p.Text : p.TextMuted;
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            int enabled = _all.Count(e => e.Enabled);
            _summary.Text = Get("startup.summary", _all.Count, enabled, _all.Count - enabled);
        }

        private void ShowDetails()
        {
            if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not StartupEntry e)
            {
                _details.Text = Get("startup.details_hint");
                return;
            }
            string nl = Environment.NewLine;
            _details.Text =
                $"{e.Name}  ·  {(e.Enabled ? Get("startup.enabled") : Get("startup.disabled"))}{(e.TargetExists ? "" : "  ·  " + Get("startup.missing"))}{nl}" +
                $"{Get("startup.col_publisher")}: {e.Publisher ?? Get("startup.unknown_publisher")}{nl}" +
                $"{Get("startup.col_source")}: {SourceText(e.Source)}  ·  {Loc.Ltr(e.Location)}{nl}" +
                $"{Get("startup.col_command")}: {Loc.Ltr(e.Command)}";
        }

        /// <summary>Read-only, selectable details of the selected row (full texts, wrapped).</summary>
        internal static TextBox DetailsBox() => new()
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            Font = UiFonts.Regular(9.5f),
            Margin = new Padding(0, 6, 0, 0)
        };

        public static string SourceText(StartupSource s) => s switch
        {
            StartupSource.RegistryUser => Get("startup.src_user_run"),
            StartupSource.RegistryMachine => Get("startup.src_machine_run"),
            StartupSource.RegistryMachine32 => Get("startup.src_machine_run32"),
            StartupSource.FolderUser => Get("startup.src_user_folder"),
            _ => Get("startup.src_common_folder")
        };

        private async Task ChangeAsync(bool enable)
        {
            var selected = _list.Items.Cast<ListViewItem>()
                .Where(i => i.Checked || i.Selected)
                .Select(i => (StartupEntry)i.Tag!)
                .Where(e => e.Enabled != enable)
                .ToList();
            if (selected.Count == 0)
            {
                _summary.Text = Get("startup.nothing_selected");
                return;
            }
            await _a.SetEnabled(selected, enable);
            await LoadAsync();
        }

        public void ApplyTheme(ThemePalette p)
        {
            BackColor = p.WindowBackground;
            _search.BackColor = p.Input;
            _search.ForeColor = p.Text;
            _details.BackColor = p.Card;
            _details.ForeColor = p.Text;
            if (_all.Count > 0) Fill();
        }
    }
}
