using System.Drawing.Drawing2D;
using KWin.Themes;

namespace KWin.Controls
{
    /// <summary>
    /// Circular usage gauge. Value is 0-100, or null when no measurement is available
    /// (then only the empty track is drawn - never a made-up value).
    /// </summary>
    public class RingGauge : Control, IThemedControl
    {
        private double? _value;
        private ThemePalette _p = ThemeManager.Current;

        public Color RingColor { get; set; } = Color.DeepSkyBlue;

        public double? Value
        {
            get => _value;
            set
            {
                double? v = value is double d ? Math.Clamp(d, 0, 100) : null;
                if (Nullable.Equals(v, _value)) return;
                _value = v;
                Invalidate();
            }
        }

        public RingGauge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            AccessibleRole = AccessibleRole.ProgressBar;
        }

        public void ApplyTheme(ThemePalette p) { _p = p; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? _p.Card);
            Draw.Smooth(g);
            float thick = Draw.S(this, 8);
            float size = Math.Min(Width, Height) - thick - 2;
            if (size <= 0) return;
            var rect = new RectangleF((Width - size) / 2f, (Height - size) / 2f, size, size);

            using (var track = new Pen(_p.Track, thick))
                g.DrawEllipse(track, rect);

            if (_value is double v && v > 0)
            {
                using var pen = new Pen(RingColor, thick) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                float sweep = (float)(v / 100.0 * 360.0);
                bool rtl = RightToLeft == RightToLeft.Yes;
                g.DrawArc(pen, rect, -90, rtl ? -sweep : sweep);
            }
            AccessibleName = _value is double val ? $"{val:0}%" : "Not available";
        }
    }

    /// <summary>Horizontal usage bar (fills from the reading-direction start).</summary>
    public class UsageBar : Control, IThemedControl
    {
        private double _percent;
        private ThemePalette _p = ThemeManager.Current;

        public Color BarColor { get; set; } = Color.Empty;

        public double Percent
        {
            get => _percent;
            set { _percent = Math.Clamp(value, 0, 100); Invalidate(); }
        }

        public UsageBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            AccessibleRole = AccessibleRole.ProgressBar;
        }

        public void ApplyTheme(ThemePalette p) { _p = p; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? _p.Card);
            Draw.Smooth(g);
            int h = Math.Min(Height, Draw.S(this, 10));
            var track = new RectangleF(0, (Height - h) / 2f, Width - 1, h);
            using (var path = Draw.Rounded(track, h / 2f))
            using (var b = new SolidBrush(_p.Track))
                g.FillPath(b, path);

            float w = (float)(track.Width * _percent / 100.0);
            if (w >= 1)
            {
                bool rtl = RightToLeft == RightToLeft.Yes;
                var fill = new RectangleF(rtl ? track.Right - w : track.X, track.Y, w, h);
                Color c = BarColor.IsEmpty ? _p.Accent : BarColor;
                using var path = Draw.Rounded(fill, h / 2f);
                using var b = new LinearGradientBrush(new RectangleF(track.X, track.Y, track.Width + 1, h),
                    rtl ? Draw.Lighten(c, 0.2f) : c, rtl ? c : Draw.Lighten(c, 0.2f), LinearGradientMode.Horizontal);
                g.FillPath(b, path);
            }
            AccessibleName = $"{_percent:0}%";
        }
    }

    /// <summary>Icon on a tinted rounded square (card headers, metric tiles, status).</summary>
    public class IconBadge : Control, IThemedControl
    {
        private ThemePalette _p = ThemeManager.Current;

        public string Glyph { get; set; } = string.Empty;
        /// <summary>Icon color; the background is a translucent version of it. Empty = accent.</summary>
        public Color Tint { get; set; } = Color.Empty;
        public float GlyphSize { get; set; } = 16f;
        /// <summary>False = draw only the glyph, no tinted square.</summary>
        public bool ShowBackground { get; set; } = true;

        public IconBadge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            AccessibleRole = AccessibleRole.Graphic;
        }

        public void ApplyTheme(ThemePalette p) { _p = p; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? _p.Card);
            Draw.Smooth(g);
            Color tint = Tint.IsEmpty ? _p.Accent : Tint;
            int side = Math.Min(Width, Height) - 1;
            var r = new Rectangle((Width - side) / 2, (Height - side) / 2, side, side);
            if (ShowBackground)
            {
                using var path = Draw.Rounded(r, Draw.S(this, 8));
                using var b = new SolidBrush(Color.FromArgb(_p.IsDark ? 48 : 30, tint));
                g.FillPath(b, path);
            }
            TextRenderer.DrawText(g, Glyph, Glyphs.Font(GlyphSize), r, tint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>The original K-Win wordmark: yellow "K", blue "-Win", plus a version badge.</summary>
    public class LogoControl : Control, IThemedControl
    {
        private ThemePalette _p = ThemeManager.Current;

        public string Badge { get; set; } = "1.5";

        public LogoControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = "K-Win";
        }

        public void ApplyTheme(ThemePalette p) { _p = p; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? _p.Sidebar);
            Draw.Smooth(g);
            var font = UiFonts.Bold(26f);
            var badgeFont = UiFonts.Semibold(10f);
            const TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

            Size k = TextRenderer.MeasureText(g, "K", font, Size.Empty, f);
            Size win = TextRenderer.MeasureText(g, "-Win", font, Size.Empty, f);
            Size bt = TextRenderer.MeasureText(g, Badge, badgeFont, Size.Empty, f);
            int badgeW = bt.Width + Draw.S(this, 10), badgeH = bt.Height + Draw.S(this, 4);
            int gap = Draw.S(this, 6);
            int total = k.Width + win.Width + gap + badgeW;
            int x = Math.Max(0, (Width - total) / 2);
            int y = Math.Max(0, (Height - k.Height) / 2);

            // The wordmark is a brand name and is always drawn left-to-right.
            TextRenderer.DrawText(g, "K", font, new Point(x, y), _p.LogoYellow, f);
            TextRenderer.DrawText(g, "-Win", font, new Point(x + k.Width, y), _p.LogoBlue, f);

            var badge = new Rectangle(x + k.Width + win.Width + gap, y + (k.Height - badgeH) / 2 + Draw.S(this, 3), badgeW, badgeH);
            using (var path = Draw.Rounded(badge, Draw.S(this, 5)))
            using (var pen = new Pen(_p.LogoBlue, Math.Max(1.5f, Draw.S(this, 2))))
                g.DrawPath(pen, path);
            TextRenderer.DrawText(g, Badge, badgeFont, badge, _p.LogoBlue,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
