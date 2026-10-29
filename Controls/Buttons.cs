using System.Drawing.Drawing2D;
using KWin.Themes;

namespace KWin.Controls
{
    /// <summary>
    /// Base for custom-drawn clickable controls: hover/pressed state, keyboard (Enter/Space),
    /// focus cue and accessibility.
    /// </summary>
    public abstract class ClickableControl : Control, IThemedControl
    {
        protected bool Hover;
        protected bool Pressed;
        protected ThemePalette Palette = ThemeManager.Current;

        protected ClickableControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable | ControlStyles.StandardClick, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
        }

        public virtual void ApplyTheme(ThemePalette p)
        {
            Palette = p;
            Invalidate();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            AccessibleName = Text.Replace("\n", " ");
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = false; Pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                e.Handled = true;
                OnClick(EventArgs.Empty);
            }
            base.OnKeyDown(e);
        }

        protected void PaintParentBackground(Graphics g) => g.Clear(Parent?.BackColor ?? Palette.WindowBackground);

        protected void DrawFocus(Graphics g, Rectangle r, float radius)
        {
            if (!Focused || !ShowFocusCues) return;
            using var pen = new Pen(Palette.Accent, 2f) { DashStyle = DashStyle.Dot };
            using var path = Draw.Rounded(new RectangleF(r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3), radius);
            g.DrawPath(pen, path);
        }
    }

    /// <summary>Sidebar navigation item (icon + text, active highlight). Also used as a segmented tab.</summary>
    public class NavButton : ClickableControl
    {
        private bool _active;

        public string Glyph { get; set; } = string.Empty;

        public bool IsActive
        {
            get => _active;
            set
            {
                _active = value;
                AccessibleDescription = value ? "Selected" : null;
                Invalidate();
            }
        }

        /// <summary>Compact style: smaller, centered text, no icon column (used for sub-page tabs).</summary>
        public bool Compact { get; set; }

        public NavButton()
        {
            AccessibleRole = AccessibleRole.PageTab;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            PaintParentBackground(g);
            Draw.Smooth(g);
            bool rtl = Draw.IsRtl(this);
            int pad = Draw.S(this, 4);
            var r = new Rectangle(pad, pad / 2, Width - pad * 2, Height - pad);
            float radius = Draw.S(this, 8);

            Color text = Palette.Text;
            if (_active)
            {
                using var path = Draw.Rounded(r, radius);
                using var brush = new LinearGradientBrush(r,
                    rtl ? Palette.AccentDark : Palette.Accent,
                    rtl ? Palette.Accent : Palette.AccentDark, LinearGradientMode.Horizontal);
                g.FillPath(brush, path);
                text = Palette.OnAccent;
            }
            else if (Hover || Pressed)
            {
                using var path = Draw.Rounded(r, radius);
                using var brush = new SolidBrush(Pressed ? Palette.CardHover : Palette.SidebarHover);
                g.FillPath(brush, path);
            }
            if (!Enabled) text = Palette.TextMuted;

            if (Compact)
            {
                TextRenderer.DrawText(g, Text, UiFonts.Semibold(9.5f), r, text,
                    Draw.TextFlags(this, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis));
            }
            else
            {
                int iconW = Draw.S(this, 44);
                var iconRect = Draw.Mirror(new Rectangle(r.X + Draw.S(this, 8), r.Y, iconW, r.Height), Width, rtl);
                var textRect = rtl
                    ? new Rectangle(r.X + Draw.S(this, 8), r.Y, iconRect.Left - r.X - Draw.S(this, 16), r.Height)
                    : new Rectangle(iconRect.Right + Draw.S(this, 8), r.Y, r.Right - iconRect.Right - Draw.S(this, 16), r.Height);

                TextRenderer.DrawText(g, Glyph, Glyphs.Font(15f), iconRect, _active ? text : Palette.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, Text, _active ? UiFonts.Semibold(11f) : UiFonts.Regular(11f), textRect, text,
                    Draw.TextFlags(this, (rtl ? TextFormatFlags.Right : TextFormatFlags.Left) |
                                         TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis));
            }

            DrawFocus(g, r, radius);
        }
    }

    /// <summary>
    /// Rounded accent button with optional icon and an optional drop-down part
    /// (raises <see cref="DropDownClick"/> when the arrow area is clicked or Alt+Down is pressed).
    /// </summary>
    public class AccentButton : ClickableControl
    {
        private Point _lastMouse;

        public string Glyph { get; set; } = string.Empty;
        public bool HasDropDown { get; set; }
        /// <summary>Custom fill; empty = theme accent.</summary>
        public Color FillColor { get; set; } = Color.Empty;
        /// <summary>Secondary style: card-colored with border (for "View All" and similar).</summary>
        public bool Secondary { get; set; }
        public float FontSize { get; set; } = 10.5f;

        public event EventHandler? DropDownClick;

        private int DropWidth => HasDropDown ? Draw.S(this, 44) : 0;

        private bool InDropDown(Point p)
        {
            if (!HasDropDown) return false;
            return Draw.IsRtl(this) ? p.X < DropWidth : p.X > Width - DropWidth;
        }

        protected override void OnMouseDown(MouseEventArgs e) { _lastMouse = e.Location; base.OnMouseDown(e); }

        protected override void OnClick(EventArgs e)
        {
            if (e is MouseEventArgs && InDropDown(_lastMouse))
            {
                DropDownClick?.Invoke(this, EventArgs.Empty);
                return;
            }
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (HasDropDown && e.Alt && e.KeyCode == Keys.Down)
            {
                e.Handled = true;
                DropDownClick?.Invoke(this, EventArgs.Empty);
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            PaintParentBackground(g);
            Draw.Smooth(g);
            bool rtl = Draw.IsRtl(this);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            float radius = Draw.S(this, Secondary ? 6 : 8);

            Color fill = FillColor.IsEmpty ? Palette.Accent : FillColor;
            Color text = Palette.OnAccent;
            using var path = Draw.Rounded(r, radius);

            if (Secondary)
            {
                fill = Hover ? Palette.CardHover : Palette.Input;
                text = Enabled ? Palette.Text : Palette.TextMuted;
                using var b = new SolidBrush(fill);
                g.FillPath(b, path);
                using var pen = new Pen(Palette.CardBorder);
                g.DrawPath(pen, path);
            }
            else
            {
                if (!Enabled) { fill = Palette.Input; text = Palette.TextMuted; } // clearly not clickable
                else if (Pressed) fill = Draw.Darken(fill, 0.15f);
                else if (Hover) fill = Draw.Lighten(fill, 0.12f);
                using var b = new LinearGradientBrush(r, Draw.Lighten(fill, 0.08f), Draw.Darken(fill, 0.12f), LinearGradientMode.Vertical);
                g.FillPath(b, path);
            }

            var main = new Rectangle(rtl ? DropWidth : 0, 0, Width - DropWidth, Height);
            if (HasDropDown)
            {
                int x = rtl ? DropWidth : Width - DropWidth;
                using var pen = new Pen(Color.FromArgb(90, Color.White));
                g.DrawLine(pen, x, Draw.S(this, 8), x, Height - Draw.S(this, 8));
                var drop = new Rectangle(rtl ? 0 : Width - DropWidth, 0, DropWidth, Height);
                TextRenderer.DrawText(g, Glyphs.ChevronDown, Glyphs.Font(9f), drop, text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            var font = Secondary ? UiFonts.Regular(FontSize) : UiFonts.Semibold(FontSize);
            Size textSize = TextRenderer.MeasureText(g, Text, font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            int glyphW = string.IsNullOrEmpty(Glyph) ? 0 : Draw.S(this, 26);
            int total = glyphW + textSize.Width;
            int start = main.X + Math.Max(Draw.S(this, 8), (main.Width - total) / 2);
            var glyphRect = new Rectangle(rtl ? start + textSize.Width : start, 0, glyphW, Height);
            var textRect = new Rectangle(rtl ? start : start + glyphW, 0, Math.Min(textSize.Width + 2, main.Right - start - glyphW), Height);

            if (glyphW > 0)
            {
                Color gc = Secondary ? Palette.Text : Palette.LogoYellow;
                TextRenderer.DrawText(g, Glyph, Glyphs.Font(FontSize + 3), glyphRect, gc,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            TextRenderer.DrawText(g, Text, font, textRect, text,
                Draw.TextFlags(this, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine));

            DrawFocus(g, r, radius);
        }
    }

    /// <summary>Colored quick-action tile (icon square + two-line caption).</summary>
    public class ActionTile : ClickableControl
    {
        public string Glyph { get; set; } = string.Empty;
        public Color BaseColor { get; set; } = Color.SteelBlue;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            PaintParentBackground(g);
            Draw.Smooth(g);
            bool rtl = Draw.IsRtl(this);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            float radius = Draw.S(this, 10);

            Color baseColor = Hover ? Draw.Lighten(BaseColor, 0.1f) : BaseColor;
            if (Pressed) baseColor = Draw.Darken(BaseColor, 0.1f);
            using (var path = Draw.Rounded(r, radius))
            using (var brush = new LinearGradientBrush(r, baseColor, Draw.Darken(baseColor, 0.35f),
                       rtl ? LinearGradientMode.BackwardDiagonal : LinearGradientMode.ForwardDiagonal))
            {
                g.FillPath(brush, path);
                using var pen = new Pen(Color.FromArgb(60, Color.White));
                g.DrawPath(pen, path);
            }

            int box = Math.Min(Height - Draw.S(this, 24), Draw.S(this, 54));
            int m = (Height - box) / 2;
            var boxRect = Draw.Mirror(new Rectangle(Draw.S(this, 12), m, box, box), Width, rtl);
            using (var path = Draw.Rounded(boxRect, Draw.S(this, 8)))
            using (var brush = new SolidBrush(Draw.Lighten(BaseColor, 0.18f)))
                g.FillPath(brush, path);
            TextRenderer.DrawText(g, Glyph, Glyphs.Font(17f), boxRect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            int textX = Draw.S(this, 12) + box + Draw.S(this, 12);
            var textRect = Draw.Mirror(new Rectangle(textX, 0, Width - textX - Draw.S(this, 8), Height), Width, rtl);
            TextRenderer.DrawText(g, Text, UiFonts.Semibold(10.5f), textRect, Color.White,
                Draw.TextFlags(this, (rtl ? TextFormatFlags.Right : TextFormatFlags.Left) |
                                     TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis));

            DrawFocus(g, r, radius);
        }
    }
}
