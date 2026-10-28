using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace KWin.Controls
{
    /// <summary>
    /// Icon glyphs from the Windows icon font (Segoe Fluent Icons on Windows 11,
    /// Segoe MDL2 Assets on Windows 10). Code points verified to render on both.
    /// </summary>
    public static class Glyphs
    {
        public const string Home = "";
        public const string Performance = "";
        public const string Cleanup = "";
        public const string Privacy = "";
        public const string Startup = "";
        public const string Health = "";
        public const string Tools = "";
        public const string Settings = "";
        public const string Cpu = "";
        public const string Memory = "";
        public const string Disk = "";
        public const string Gpu = "";
        public const string Pc = "";
        public const string History = "";
        public const string Clock = "";
        public const string Bolt = "";
        public const string CheckCircle = "";
        public const string Info = "";
        public const string Warning = "";
        public const string Error = "";
        public const string Moon = "";
        public const string Sun = "";
        public const string Globe = "";
        public const string Undo = "";
        public const string Document = "";
        public const string ChevronDown = "";
        public const string Admin = "";
        public const string Heart = "";
        public const string Rocket = "";

        private static readonly Lazy<string> _family = new(() =>
        {
            using var fonts = new InstalledFontCollection();
            var names = fonts.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return names.Contains("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        });

        public static string FontFamily => _family.Value;

        public static Font Font(float size) => UiFonts.Get(FontFamily, size, FontStyle.Regular);
    }

    /// <summary>Shared, cached fonts (they live for the whole app session).</summary>
    public static class UiFonts
    {
        private static readonly Dictionary<(string, float, FontStyle), Font> _cache = new();

        private static readonly Lazy<(string Text, string Semibold)> _families = new(() =>
        {
            using var fonts = new InstalledFontCollection();
            var names = fonts.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            string text = names.Contains("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
            string semi = names.Contains("Segoe UI Variable Display Semib") ? "Segoe UI Variable Display Semib"
                        : names.Contains("Segoe UI Semibold") ? "Segoe UI Semibold" : "Segoe UI";
            return (text, semi);
        });

        public static Font Get(string family, float size, FontStyle style)
        {
            lock (_cache)
            {
                if (!_cache.TryGetValue((family, size, style), out var f))
                {
                    f = new Font(family, size, style, GraphicsUnit.Point);
                    _cache[(family, size, style)] = f;
                }
                return f;
            }
        }

        public static Font Regular(float size) => Get(_families.Value.Text, size, FontStyle.Regular);
        public static Font Semibold(float size) => Get(_families.Value.Semibold, size, FontStyle.Regular);
        public static Font Bold(float size) => Get("Segoe UI", size, FontStyle.Bold);
    }

    /// <summary>Drawing helpers shared by the custom controls.</summary>
    public static class Draw
    {
        /// <summary>Scales a 96-DPI pixel value to the control's current DPI.</summary>
        public static int S(Control c, float px) => (int)Math.Round(px * c.DeviceDpi / 96f);

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Max(1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t),
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));

        public static Color Lighten(Color c, float t) => Mix(c, Color.White, t);
        public static Color Darken(Color c, float t) => Mix(c, Color.Black, t);

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        /// <summary>Text flags that respect right-to-left reading order.</summary>
        public static TextFormatFlags TextFlags(Control c, TextFormatFlags flags)
        {
            if (c.RightToLeft == RightToLeft.Yes)
                flags |= TextFormatFlags.RightToLeft;
            return flags | TextFormatFlags.NoPrefix;
        }

        public static bool IsRtl(Control c) => c.RightToLeft == RightToLeft.Yes;

        /// <summary>Mirrors a rectangle horizontally inside a container width (for right-to-left layouts).</summary>
        public static Rectangle Mirror(Rectangle r, int containerWidth, bool rtl) =>
            rtl ? new Rectangle(containerWidth - r.Right, r.Y, r.Width, r.Height) : r;
    }
}
