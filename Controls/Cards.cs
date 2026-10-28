using KWin.Themes;

namespace KWin.Controls
{
    public enum SurfaceKind
    {
        /// <summary>Rounded card with border (dashboard cards).</summary>
        Card,
        /// <summary>Sidebar background (no rounding).</summary>
        Sidebar,
        /// <summary>Window background (no rounding, no border).</summary>
        Window
    }

    /// <summary>
    /// Themed container. Cards are drawn with rounded corners and a subtle border;
    /// child controls inherit the card color through the ambient BackColor.
    /// </summary>
    public class RoundedPanel : Panel, IThemedControl
    {
        private Color _fill = Color.Empty;
        private Color _border = Color.Empty;

        public SurfaceKind Kind { get; set; } = SurfaceKind.Card;
        public int Radius { get; set; } = 12;

        public RoundedPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public virtual void ApplyTheme(ThemePalette p)
        {
            _fill = Kind switch
            {
                SurfaceKind.Sidebar => p.Sidebar,
                SurfaceKind.Window => p.WindowBackground,
                _ => p.Card
            };
            _border = p.CardBorder;
            BackColor = _fill;
            ForeColor = p.Text;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (Kind != SurfaceKind.Card)
            {
                g.Clear(BackColor);
                if (Kind == SurfaceKind.Sidebar)
                {
                    // Divider on the edge that touches the content area.
                    using var pen = new Pen(_border);
                    int x = Draw.IsRtl(this) ? 0 : Width - 1;
                    g.DrawLine(pen, x, 0, x, Height);
                }
                return;
            }

            g.Clear(Parent?.BackColor ?? BackColor);
            Draw.Smooth(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using var path = Draw.Rounded(r, Draw.S(this, Radius));
            using var brush = new SolidBrush(_fill.IsEmpty ? BackColor : _fill);
            g.FillPath(brush, path);
            using var pen2 = new Pen(_border.IsEmpty ? BackColor : _border);
            g.DrawPath(pen2, path);
        }
    }

    /// <summary>
    /// Hosts 1.0 page content (themed by the legacy theming code).
    /// Window mirroring (WS_EX_LAYOUTRTL) is deliberately not used: it caused black rectangles and
    /// overlapping text with these controls. In Arabic, text reads right-to-left and table columns
    /// are mirrored; positions inside groups stay as designed until those pages are rebuilt.
    /// </summary>
    public class LegacyHostPanel : Panel, ILegacyThemeHost
    {
    }

    /// <summary>
    /// Drop-down list drawn with the theme colors (the standard ComboBox keeps light system
    /// colors in dark mode).
    /// </summary>
    public class ThemedComboBox : ComboBox, IThemedControl
    {
        private ThemePalette _p = ThemeManager.Current;

        public ThemedComboBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            ItemHeight = 22;
        }

        public void ApplyTheme(ThemePalette p)
        {
            _p = p;
            BackColor = p.Input;
            ForeColor = p.Text;
            Invalidate();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var b = new SolidBrush(selected ? _p.Accent : _p.Input))
                e.Graphics.FillRectangle(b, e.Bounds);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
                        (RightToLeft == RightToLeft.Yes ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left);
            var r = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, r, selected ? _p.OnAccent : _p.Text, flags);
        }
    }

    public enum TextRole { Primary, Muted, Accent, Success, Warning, Error, OnAccent }

    /// <summary>Label whose color comes from the theme.</summary>
    public class ThemedLabel : Label, IThemedControl
    {
        private TextRole _role = TextRole.Primary;

        public TextRole Role
        {
            get => _role;
            set { _role = value; ApplyTheme(ThemeManager.Current); }
        }

        public ThemedLabel()
        {
            UseMnemonic = false;
            AutoEllipsis = true;
        }

        public virtual void ApplyTheme(ThemePalette p)
        {
            ForeColor = _role switch
            {
                TextRole.Muted => p.TextMuted,
                TextRole.Accent => p.Accent,
                TextRole.Success => p.Success,
                TextRole.Warning => p.Warning,
                TextRole.Error => p.Error,
                TextRole.OnAccent => p.OnAccent,
                _ => p.Text
            };
        }

        /// <summary>A label showing one icon glyph.</summary>
        public static ThemedLabel Glyph(string glyph, float size, TextRole role = TextRole.Primary) => new()
        {
            Text = glyph,
            Font = Glyphs.Font(size),
            Role = role,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = false
        };
    }
}
