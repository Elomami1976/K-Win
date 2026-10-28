using System.Runtime.InteropServices;

namespace KWin.Themes
{
    /// <summary>All colors used by the K-Win 1.5 interface for one theme.</summary>
    public sealed class ThemePalette
    {
        public bool IsDark { get; init; }

        public Color WindowBackground { get; init; }
        public Color Sidebar { get; init; }
        public Color SidebarHover { get; init; }
        public Color Card { get; init; }
        public Color CardBorder { get; init; }
        public Color CardHover { get; init; }
        public Color Input { get; init; }
        public Color Track { get; init; }

        public Color Text { get; init; }
        public Color TextMuted { get; init; }

        public Color Accent { get; init; }
        public Color AccentDark { get; init; }
        public Color OnAccent { get; init; }
        public Color LogoYellow { get; init; }
        public Color LogoBlue { get; init; }

        public Color Success { get; init; }
        public Color Warning { get; init; }
        public Color Error { get; init; }
        public Color Purple { get; init; }
    }

    /// <summary>Anything that repaints itself from the current palette.</summary>
    public interface IThemedControl
    {
        void ApplyTheme(ThemePalette palette);
    }

    /// <summary>Marks a container whose children are themed by the legacy (1.0) theming code.</summary>
    public interface ILegacyThemeHost { }

    /// <summary>
    /// Holds the current theme (dark or light) and applies it to control trees and the window title bar.
    /// </summary>
    public static class ThemeManager
    {
        public static readonly ThemePalette Dark = new()
        {
            IsDark = true,
            WindowBackground = ColorTranslator.FromHtml("#0B1220"),
            Sidebar = ColorTranslator.FromHtml("#101B30"),
            SidebarHover = ColorTranslator.FromHtml("#18263F"),
            Card = ColorTranslator.FromHtml("#162238"),
            CardBorder = ColorTranslator.FromHtml("#24344F"),
            CardHover = ColorTranslator.FromHtml("#1D2C47"),
            Input = ColorTranslator.FromHtml("#1B2A44"),
            Track = ColorTranslator.FromHtml("#26364F"),
            Text = ColorTranslator.FromHtml("#EAF0FA"),
            TextMuted = ColorTranslator.FromHtml("#97A8C3"),
            Accent = ColorTranslator.FromHtml("#00A8FF"),
            AccentDark = ColorTranslator.FromHtml("#0A6FD6"),
            OnAccent = Color.White,
            LogoYellow = ColorTranslator.FromHtml("#FFD84D"),
            LogoBlue = ColorTranslator.FromHtml("#00A8FF"),
            Success = ColorTranslator.FromHtml("#20C997"),
            Warning = ColorTranslator.FromHtml("#FFB020"),
            Error = ColorTranslator.FromHtml("#F05454"),
            Purple = ColorTranslator.FromHtml("#B15CFF")
        };

        public static readonly ThemePalette Light = new()
        {
            IsDark = false,
            WindowBackground = ColorTranslator.FromHtml("#EEF2F8"),
            Sidebar = ColorTranslator.FromHtml("#FFFFFF"),
            SidebarHover = ColorTranslator.FromHtml("#E8F1FC"),
            Card = ColorTranslator.FromHtml("#FFFFFF"),
            CardBorder = ColorTranslator.FromHtml("#D9E1EC"),
            CardHover = ColorTranslator.FromHtml("#F4F8FD"),
            Input = ColorTranslator.FromHtml("#F4F7FB"),
            Track = ColorTranslator.FromHtml("#DDE5F0"),
            Text = ColorTranslator.FromHtml("#0B1220"),
            TextMuted = ColorTranslator.FromHtml("#56657E"),
            Accent = ColorTranslator.FromHtml("#0078D4"),
            AccentDark = ColorTranslator.FromHtml("#005A9E"),
            OnAccent = Color.White,
            LogoYellow = ColorTranslator.FromHtml("#E2A800"),
            LogoBlue = ColorTranslator.FromHtml("#0078D4"),
            Success = ColorTranslator.FromHtml("#0E8F6A"),
            Warning = ColorTranslator.FromHtml("#B86E00"),
            Error = ColorTranslator.FromHtml("#C7303A"),
            Purple = ColorTranslator.FromHtml("#7B3FE4")
        };

        public static ThemePalette Current { get; private set; } = Dark;

        public static event Action? ThemeChanged;

        public static void SetTheme(bool dark)
        {
            var next = dark ? Dark : Light;
            if (ReferenceEquals(next, Current)) return;
            Current = next;
            ThemeChanged?.Invoke();
        }

        /// <summary>
        /// Applies the current palette to every <see cref="IThemedControl"/> in the tree.
        /// Subtrees under an <see cref="ILegacyThemeHost"/> are left to the legacy theming code.
        /// </summary>
        public static void ApplyTree(Control root)
        {
            if (root is IThemedControl themed)
                themed.ApplyTheme(Current);
            if (root is ILegacyThemeHost)
                return;
            foreach (Control child in root.Controls)
                ApplyTree(child);
        }

        #region Title bar

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35; // Windows 11 only
        private const int DWMWA_TEXT_COLOR = 36;    // Windows 11 only

        /// <summary>
        /// Makes the native title bar match the theme (dark mode on Windows 10 20H1+ and 11;
        /// exact caption color on Windows 11). The native title bar is kept so snapping,
        /// resizing, keyboard shortcuts and accessibility behave exactly like Windows.
        /// </summary>
        public static void ApplyTitleBar(Form form)
        {
            if (!form.IsHandleCreated) return;
            try
            {
                int dark = Current.IsDark ? 1 : 0;
                if (DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));

                int caption = ToColorRef(Current.Sidebar);
                DwmSetWindowAttribute(form.Handle, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
                int text = ToColorRef(Current.Text);
                DwmSetWindowAttribute(form.Handle, DWMWA_TEXT_COLOR, ref text, sizeof(int));
            }
            catch
            {
                // Older Windows: the title bar keeps its default look.
            }
        }

        private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

        #endregion
    }
}
