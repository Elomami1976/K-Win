using System.Drawing.Drawing2D;
using KWin.Services;
using KWin.Themes;

namespace KWin.Controls
{
    /// <summary>
    /// Small history chart (0-100 scale) drawn from a <see cref="MetricHistory"/>.
    /// Missing measurements are drawn as gaps, never interpolated. One buffer is reused for every
    /// repaint, so drawing does not allocate per sample (apart from short-lived GDI+ objects).
    /// </summary>
    public class Sparkline : Control, IThemedControl
    {
        private readonly double[] _buffer = new double[SystemMonitorService.HistoryLength];
        private readonly PointF[] _points = new PointF[SystemMonitorService.HistoryLength];
        private ThemePalette _p = ThemeManager.Current;

        public MetricHistory? Source { get; set; }
        public Color LineColor { get; set; } = Color.DeepSkyBlue;
        /// <summary>Optional small caption drawn in the corner (e.g. "Activity").</summary>
        public string Caption { get; set; } = string.Empty;

        public Sparkline()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            AccessibleRole = AccessibleRole.Chart;
        }

        public void ApplyTheme(ThemePalette p) { _p = p; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? _p.Card);
            if (Width < 4 || Height < 4) return;
            Draw.Smooth(g);

            // Faint baseline so an empty chart still reads as a chart.
            using (var basePen = new Pen(Color.FromArgb(70, _p.Track)))
                g.DrawLine(basePen, 0, Height - 1, Width, Height - 1);

            if (!string.IsNullOrEmpty(Caption))
            {
                var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                            (RightToLeft == RightToLeft.Yes ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left);
                TextRenderer.DrawText(g, Caption, UiFonts.Regular(7.5f), new Rectangle(0, 0, Width, Height), _p.TextMuted, flags);
            }

            int n = Source?.CopyTo(_buffer) ?? 0;
            if (n < 2) return;

            int capacity = _buffer.Length;
            float step = (Width - 1) / (float)(capacity - 1);
            float top = 2, bottom = Height - 2;
            float xOffset = (capacity - n) * step; // newest value always at the right edge

            using var pen = new Pen(LineColor, Math.Max(1.5f, Draw.S(this, 2))) { LineJoin = LineJoin.Round };
            using var fill = new LinearGradientBrush(new Rectangle(0, 0, Width, Height),
                Color.FromArgb(90, LineColor), Color.FromArgb(0, LineColor), LinearGradientMode.Vertical);

            // Draw each run of consecutive measured values; NaN breaks the line (unavailable data).
            int runStart = -1;
            for (int i = 0; i <= n; i++)
            {
                bool valid = i < n && !double.IsNaN(_buffer[i]);
                if (valid && runStart < 0) runStart = i;
                if (!valid && runStart >= 0)
                {
                    DrawRun(g, pen, fill, runStart, i - 1, xOffset, step, top, bottom);
                    runStart = -1;
                }
            }
        }

        private void DrawRun(Graphics g, Pen pen, Brush fill, int from, int to, float xOffset, float step, float top, float bottom)
        {
            int count = to - from + 1;
            for (int i = 0; i < count; i++)
            {
                double v = Math.Clamp(_buffer[from + i], 0, 100);
                _points[i] = new PointF(xOffset + (from + i) * step, bottom - (float)(v / 100.0) * (bottom - top));
            }

            if (count == 1)
            {
                g.FillEllipse(pen.Brush, _points[0].X - 1.5f, _points[0].Y - 1.5f, 3, 3);
                return;
            }

            var span = _points.AsSpan(0, count).ToArray();
            using (var area = new GraphicsPath())
            {
                area.AddLines(span);
                area.AddLine(span[^1].X, span[^1].Y, span[^1].X, bottom + 2);
                area.AddLine(span[^1].X, bottom + 2, span[0].X, bottom + 2);
                area.CloseFigure();
                g.FillPath(fill, area);
            }
            g.DrawLines(pen, span);
        }
    }
}
