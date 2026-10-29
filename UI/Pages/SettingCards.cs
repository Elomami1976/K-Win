using KWin.Controls;
using KWin.Models;
using KWin.Services;
using KWin.Themes;
using KWin.Utils;
using static KWin.Utils.Loc;

namespace KWin.UI.Pages
{
    /// <summary>
    /// Card with a title, current-state badge, description, a list of facts (recommended state,
    /// benefit, side effects, permissions, undo), optional custom content and action buttons.
    /// </summary>
    public class ActionCard : RoundedPanel
    {
        public const int DefaultHeight = 304;
        private const int LabelColumn = 132;

        private readonly TableLayoutPanel _grid;
        private readonly ThemedLabel _state, _desc;
        private readonly TableLayoutPanel _facts;
        private readonly Panel _extra;
        private readonly FlowLayoutPanel _buttons;
        private readonly Dictionary<string, ThemedLabel> _factValues = new();
        private readonly List<ThemedLabel> _factOrder = new();
        private readonly ToolTip _tips = new();

        private static Font TextFont => UiFonts.Regular(9.5f);

        public ActionCard(string glyph, string title, string description)
        {
            Kind = SurfaceKind.Card;
            Dock = DockStyle.Fill;
            Margin = new Padding(6);

            _grid = Ui.Grid(1, 5, new Padding(16, 12, 16, 10));
            Ui.Rows(_grid, (SizeType.Absolute, 32), (SizeType.Absolute, 40), (SizeType.Absolute, 100), (SizeType.AutoSize, 0), (SizeType.Absolute, 44));

            var header = Ui.Grid(2, 1);
            Ui.Columns(header, (SizeType.Absolute, 34), (SizeType.Percent, 100));
            header.Controls.Add(new IconBadge { Glyph = glyph, GlyphSize = 13f, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 6, 2) }, 0, 0);
            var titleLabel = Ui.Text(title, UiFonts.Semibold(11.5f));
            _tips.SetToolTip(titleLabel, title);
            header.Controls.Add(titleLabel, 1, 0);
            _grid.Controls.Add(header, 0, 0);

            // Description and facts wrap fully; the card grows to fit (see LayoutFor).
            _desc = Ui.Text(description, TextFont, TextRole.Muted, wrap: true);
            _grid.Controls.Add(_desc, 0, 1);

            _facts = Ui.Grid(2, 0);
            _facts.ColumnStyles.Clear();
            Ui.Columns(_facts, (SizeType.Absolute, LabelColumn), (SizeType.Percent, 100));
            _facts.RowStyles.Clear();
            _grid.Controls.Add(_facts, 0, 2);

            _extra = new Panel { Dock = DockStyle.Fill, AutoSize = true, Height = 0, Margin = new Padding(0, 0, 0, 2) };
            _grid.Controls.Add(_extra, 0, 3);

            _buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 4, 0, 0) };
            _grid.Controls.Add(_buttons, 0, 4);

            Controls.Add(_grid);

            // The current state is the first fact (instead of a badge next to the title, which
            // squeezed long titles and long translations).
            SetFact(CurrentLabel, "—", TextRole.Muted);
            _state = _factValues[CurrentLabel];
            _state.Font = UiFonts.Semibold(9.5f);
        }

        private static string CurrentLabel => Get("opt.col_current");

        /// <summary>Adds or updates one fact row ("Benefit: ...").</summary>
        public void SetFact(string label, string value, TextRole role = TextRole.Primary)
        {
            if (_factValues.TryGetValue(label, out var existing))
            {
                existing.Text = value;
                existing.Role = role;
                return;
            }
            int row = _facts.RowCount++;
            _facts.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            _facts.Controls.Add(Ui.Text(label, TextFont, TextRole.Muted, wrap: true), 0, row);
            var v = Ui.Text(value, TextFont, role, wrap: true);
            _facts.Controls.Add(v, 1, row);
            _factValues[label] = v;
            _factOrder.Add(v);
            ThemeManager.ApplyTree(_facts);
        }

        private int S(int px) => Draw.S(this, px);

        private static int Measure(string text, Font font, int width) =>
            string.IsNullOrEmpty(text) || width <= 0 ? 0 :
            TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl).Height;

        /// <summary>
        /// Lays the card out for a given width (DPI-aware) so every text is fully visible, and
        /// returns the height the card needs.
        /// </summary>
        public int LayoutFor(int width)
        {
            int inner = width - Margin.Horizontal - S(32);
            int descH = Measure(_desc.Text, TextFont, inner) + S(6);
            _grid.RowStyles[1].Height = Math.Max(S(20), descH);

            _facts.ColumnStyles[0].Width = S(LabelColumn);
            int valueWidth = inner - S(LabelColumn);
            int factsH = 0;
            for (int i = 0; i < _factOrder.Count && i < _facts.RowStyles.Count; i++)
            {
                var labelCtl = _facts.GetControlFromPosition(0, i);
                int h = Math.Max(Measure(_factOrder[i].Text, TextFont, valueWidth), Measure(labelCtl?.Text ?? "", TextFont, S(LabelColumn) - S(6)));
                h = Math.Max(S(22), h + S(5));
                _facts.RowStyles[i].Height = h;
                factsH += h;
            }
            _grid.RowStyles[2].Height = factsH + S(4);

            int lineWidth = 0, lines = 1;
            foreach (Control b in _buttons.Controls)
            {
                int w = b.Width + b.Margin.Horizontal;
                if (lineWidth > 0 && lineWidth + w > inner) { lines++; lineWidth = 0; }
                lineWidth += w;
            }
            int buttonsH = lines * S(42) + S(4);
            _grid.RowStyles[4].Height = buttonsH;

            return S(12) + S(10) + S(32) + (int)_grid.RowStyles[1].Height + factsH + S(4) + _extra.Height + S(4) + buttonsH + Margin.Vertical;
        }

        /// <summary>The badge in the top corner: current state.</summary>
        public void SetState(string text, TextRole role)
        {
            _state.Text = text;
            _state.Role = role;
        }

        public AccentButton AddButton(string text, bool primary, Action onClick)
        {
            var b = new AccentButton
            {
                Text = text,
                Secondary = !primary,
                FontSize = 9.5f,
                Height = 34,
                Margin = new Padding(0, 0, 8, 0)
            };
            using (var g = CreateGraphics())
                b.Width = Math.Max(TextRenderer.MeasureText(g, text, UiFonts.Semibold(9.5f)).Width + Draw.S(this, 36), Draw.S(this, 110));
            b.Click += (s, e) => onClick();
            _buttons.Controls.Add(b);
            ThemeManager.ApplyTree(b);
            return b;
        }

        /// <summary>A secondary action shown on its own row (keeps the main button row from overflowing).</summary>
        public AccentButton AddExtraAction(string text, Action onClick)
        {
            var b = new AccentButton { Text = text, Secondary = true, FontSize = 9f, Height = 28, Dock = DockStyle.Left, Margin = new Padding(0) };
            using (var g = CreateGraphics())
                b.Width = TextRenderer.MeasureText(g, text, UiFonts.Regular(9f)).Width + Draw.S(this, 30);
            b.Click += (s, e) => onClick();
            var row = new Panel { Height = 32 };
            row.Controls.Add(b);
            SetExtra(row);
            return b;
        }

        /// <summary>Custom content between facts and buttons (e.g. a drop-down).</summary>
        public void SetExtra(Control content)
        {
            _extra.Controls.Clear();
            content.Dock = DockStyle.Top;
            _extra.Controls.Add(content);
            _extra.Height = content.Height;
            ThemeManager.ApplyTree(content);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tips.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Card for a data-driven on/off <see cref="SettingDefinition"/>.</summary>
    public sealed class SettingCard : ActionCard
    {
        private readonly SettingDefinition _def;
        private readonly Func<SettingDefinition, SettingState> _read;
        private readonly AccentButton _onButton, _offButton;
        private readonly LockReason _lock;

        /// <summary>Policy lock check (organization / local Group Policy); set by the main window.</summary>
        public static Func<SettingDefinition, LockReason> LockCheck { get; set; } = _ => LockReason.None;

        public SettingDefinition Definition => _def;

        /// <param name="read">Reads the current state.</param>
        /// <param name="apply">Applies On (true) or Off (false) - with preview, safety workflow and undo.</param>
        /// <param name="undoText">Override for the "Undo" fact (settings with non-registry steps).</param>
        public SettingCard(SettingDefinition def, Func<SettingDefinition, SettingState> read, Func<SettingDefinition, bool, Task> apply,
            string? undoText = null)
            : base(def.Glyph, Get(def.TextKey("title")), Get(def.TextKey("desc")))
        {
            _def = def;
            _read = read;
            _lock = LockCheck(def);

            string on = Get(def.TextKey("on")), off = Get(def.TextKey("off"));
            SetFact(Get("card.recommended"), def.RecommendOn switch
            {
                true => on,
                false => off,
                null => Get("card.preference")
            });
            SetFact(Get("card.benefit"), Get(def.TextKey("benefit")));
            SetFact(Get("card.side_effects"), Get(def.TextKey("side")));

            string requires = def.RequiresAdmin ? Get("card.requires_admin") : Get("card.requires_user");
            if (def.RestartRequired) requires += " · " + Get("card.restart_needed");
            else if (def.SignOutRequired) requires += " · " + Get("card.signout_needed");
            SetFact(Get("card.requires"), requires);
            SetFact(Get("card.undo"), undoText ?? Get("card.undo_full"));

            _onButton = AddButton(Get("card.switch_to", on), def.RecommendOn != false, () => _ = apply(def, true));
            _offButton = AddButton(Get("card.switch_to", off), def.RecommendOn == false, () => _ = apply(def, false));
            RefreshState();
        }

        public void RefreshState()
        {
            if (_lock != LockReason.None)
            {
                bool org = _lock == LockReason.Organization;
                SetState(Get(org ? "card.managed" : "card.locked_local"), TextRole.Warning);
                SetFact(Get("card.requires"), Get(org ? "card.managed_desc" : "card.locked_local_desc"), TextRole.Warning);
                _onButton.Enabled = _offButton.Enabled = false;
                return;
            }
            if (_def.MinBuild > 0 && WindowsVersionChecker.CurrentBuild < _def.MinBuild)
            {
                SetState(Get("card.not_applicable"), TextRole.Muted);
                _onButton.Enabled = _offButton.Enabled = false;
                return;
            }

            var state = _read(_def);
            string text = state switch
            {
                SettingState.On => Get(_def.TextKey("on")),
                SettingState.Off => Get(_def.TextKey("off")),
                SettingState.Custom => Get("card.state_custom"),
                _ => Get("card.state_unknown")
            };
            bool matchesRecommendation = _def.RecommendOn is bool rec && (rec ? state == SettingState.On : state == SettingState.Off);
            SetState(Get("card.current", text), matchesRecommendation ? TextRole.Success : state == SettingState.Unknown ? TextRole.Muted : TextRole.Accent);
            _onButton.Enabled = state != SettingState.On;
            _offButton.Enabled = state != SettingState.Off;
        }
    }

    /// <summary>
    /// Scrollable page of cards in two columns (one column when the window is narrow), with an
    /// optional notice at the top.
    /// </summary>
    public class CardPage : UserControl, IThemedControl
    {
        private readonly TableLayoutPanel _grid;
        private readonly List<Control> _cards = new();
        private readonly Panel _notices;
        private int _columns = 2;

        public int CardHeight { get; set; } = ActionCard.DefaultHeight;

        public CardPage()
        {
            DoubleBuffered = true;
            AutoScroll = true;
            _grid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0) };
            _notices = new Panel { Dock = DockStyle.Top, AutoSize = true, Height = 0 };
            Controls.Add(_grid);
            Controls.Add(_notices);
            Resize += (s, e) => Relayout();
            DpiChangedAfterParent += (s, e) => Relayout();
        }

        public void AddNotice(string text, TextRole role)
        {
            var l = new ThemedLabel
            {
                Text = text,
                Role = role,
                Font = UiFonts.Regular(9.5f),
                Dock = DockStyle.Top,
                Height = Draw.S(this, 34),
                AutoEllipsis = true,
                Padding = new Padding(6, 0, 6, 6),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _notices.Controls.Add(l);
            _notices.Height = _notices.Controls.Cast<Control>().Sum(c => c.Height);
        }

        public void AddCard(Control card)
        {
            card.Dock = DockStyle.Fill;
            _cards.Add(card);
            Relayout();
        }

        public IEnumerable<T> Cards<T>() => _cards.OfType<T>();

        private bool _inLayout;

        /// <summary>Call after card texts change (state refresh) so rows fit the new text.</summary>
        public void Relayout()
        {
            if (_inLayout || _cards.Count == 0) return;
            _inLayout = true;
            try
            {
                int width = ClientSize.Width - (VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth);
                int columns = width < Draw.S(this, 860) ? 1 : 2;
                bool rebuild = columns != _columns || _grid.Controls.Count != _cards.Count;
                _columns = columns;
                int rows = (_cards.Count + columns - 1) / columns;
                int cellWidth = Math.Max(Draw.S(this, 300), width / columns);

                _grid.SuspendLayout();
                if (rebuild)
                {
                    _grid.Controls.Clear();
                    _grid.ColumnStyles.Clear();
                    _grid.ColumnCount = columns;
                    for (int c = 0; c < columns; c++) _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
                    _grid.RowStyles.Clear();
                    _grid.RowCount = Math.Max(1, rows);
                    for (int r = 0; r < rows; r++) _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Draw.S(this, CardHeight)));
                    for (int i = 0; i < _cards.Count; i++) _grid.Controls.Add(_cards[i], i % columns, i / columns);
                }

                int total = 0;
                for (int r = 0; r < rows; r++)
                {
                    int h = Draw.S(this, 180);
                    for (int c = 0; c < columns; c++)
                    {
                        int i = r * columns + c;
                        if (i < _cards.Count && _cards[i] is ActionCard card)
                            h = Math.Max(h, card.LayoutFor(cellWidth));
                    }
                    _grid.RowStyles[r].Height = h;
                    total += h;
                }
                _grid.Height = total + 4;
                _grid.ResumeLayout(true);
            }
            finally
            {
                _inLayout = false;
            }
        }

        public virtual void ApplyTheme(ThemePalette p)
        {
            BackColor = p.WindowBackground;
            ForeColor = p.Text;
        }
    }
}
