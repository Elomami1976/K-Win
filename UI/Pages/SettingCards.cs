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

        private readonly ThemedLabel _state;
        private readonly TableLayoutPanel _facts;
        private readonly Panel _extra;
        private readonly FlowLayoutPanel _buttons;
        private readonly Dictionary<string, ThemedLabel> _factValues = new();
        private readonly ToolTip _tips = new();

        public ActionCard(string glyph, string title, string description)
        {
            Kind = SurfaceKind.Card;
            Dock = DockStyle.Fill;
            Margin = new Padding(6);

            var grid = Ui.Grid(1, 5, new Padding(16, 12, 16, 10));
            Ui.Rows(grid, (SizeType.Absolute, 32), (SizeType.Absolute, 40), (SizeType.Percent, 100), (SizeType.AutoSize, 0), (SizeType.Absolute, 44));

            var header = Ui.Grid(3, 1);
            Ui.Columns(header, (SizeType.Absolute, 34), (SizeType.Percent, 100), (SizeType.AutoSize, 0));
            header.Controls.Add(new IconBadge { Glyph = glyph, GlyphSize = 13f, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 6, 2) }, 0, 0);
            header.Controls.Add(Ui.Text(title, UiFonts.Semibold(11.5f)), 1, 0);
            _state = new ThemedLabel
            {
                AutoSize = true,
                AutoEllipsis = false,
                Font = UiFonts.Semibold(9f),
                Margin = new Padding(8, 7, 0, 0),
                Role = TextRole.Muted
            };
            header.Controls.Add(_state, 2, 0);
            grid.Controls.Add(header, 0, 0);

            var desc = Ui.Text(description, UiFonts.Regular(9f), TextRole.Muted, wrap: true);
            _tips.SetToolTip(desc, description);
            grid.Controls.Add(desc, 0, 1);

            _facts = Ui.Grid(2, 0);
            _facts.ColumnStyles.Clear();
            Ui.Columns(_facts, (SizeType.Absolute, 118), (SizeType.Percent, 100));
            _facts.RowStyles.Clear();
            grid.Controls.Add(_facts, 0, 2);

            _extra = new Panel { Dock = DockStyle.Fill, AutoSize = true, Height = 0, Margin = new Padding(0, 0, 0, 2) };
            grid.Controls.Add(_extra, 0, 3);

            _buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
            grid.Controls.Add(_buttons, 0, 4);

            Controls.Add(grid);
        }

        /// <summary>Adds or updates one fact row ("Benefit: ...").</summary>
        public void SetFact(string label, string value, TextRole role = TextRole.Primary)
        {
            if (_factValues.TryGetValue(label, out var existing))
            {
                existing.Text = value;
                existing.Role = role;
                _tips.SetToolTip(existing, value);
                return;
            }
            int row = _facts.RowCount++;
            _facts.RowStyles.Add(new RowStyle(SizeType.Absolute, 21));
            _facts.Controls.Add(Ui.Text(label, UiFonts.Regular(9f), TextRole.Muted), 0, row);
            var v = Ui.Text(value, UiFonts.Regular(9f), role);
            _tips.SetToolTip(v, value);
            _facts.Controls.Add(v, 1, row);
            _factValues[label] = v;
            ThemeManager.ApplyTree(_facts);
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
                b.Width = Math.Clamp(TextRenderer.MeasureText(g, text, UiFonts.Semibold(9.5f)).Width + Draw.S(this, 36), Draw.S(this, 110), Draw.S(this, 320));
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
        private readonly bool _locked;

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
            _locked = def.IsPolicy && OrgManagement.IsManaged;

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
            if (_locked)
            {
                SetState(Get("card.managed"), TextRole.Warning);
                SetFact(Get("card.requires"), Get("card.managed_desc"), TextRole.Warning);
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

        private void Relayout()
        {
            int columns = ClientSize.Width < Draw.S(this, 860) ? 1 : 2;
            bool changed = columns != _columns || _grid.Controls.Count != _cards.Count;
            _columns = columns;
            if (!changed && _grid.RowCount == (_cards.Count + columns - 1) / columns) return;

            _grid.SuspendLayout();
            _grid.Controls.Clear();
            _grid.ColumnStyles.Clear();
            _grid.RowStyles.Clear();
            _grid.ColumnCount = columns;
            for (int c = 0; c < columns; c++) _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            int rows = (_cards.Count + columns - 1) / columns;
            _grid.RowCount = Math.Max(1, rows);
            int h = Draw.S(this, CardHeight);
            for (int r = 0; r < rows; r++) _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            for (int i = 0; i < _cards.Count; i++) _grid.Controls.Add(_cards[i], i % columns, i / columns);
            _grid.Height = rows * h + 4;
            _grid.ResumeLayout(true);
        }

        public virtual void ApplyTheme(ThemePalette p)
        {
            BackColor = p.WindowBackground;
            ForeColor = p.Text;
        }
    }
}
