namespace KWin.UI.Pages
{
    /// <summary>Navigation order of the main window. Also used by "--tab N" after elevation.</summary>
    public static class PageIds
    {
        public const int Dashboard = 0;
        public const int Performance = 1;
        public const int Cleanup = 2;
        public const int Privacy = 3;
        public const int Startup = 4;
        public const int Health = 5;
        public const int Tools = 6;
        public const int Settings = 7;
        public const int Count = 8;
    }

    /// <summary>Small helpers for building themed layouts in code.</summary>
    internal static class Ui
    {
        public static TableLayoutPanel Grid(int columns, int rows, Padding? padding = null)
        {
            var t = new TableLayoutPanel
            {
                ColumnCount = columns,
                RowCount = rows,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = padding ?? new Padding(0)
                // BackColor left unset: it inherits the parent card's color.
            };
            // Explicit sizes by default; Columns()/Rows() replace them where a layout needs more control.
            for (int c = 0; c < columns; c++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            for (int r = 0; r < rows; r++) t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));
            return t;
        }

        public static void Columns(TableLayoutPanel t, params (SizeType Type, float Value)[] cols)
        {
            t.ColumnStyles.Clear();
            foreach (var c in cols) t.ColumnStyles.Add(new ColumnStyle(c.Type, c.Value));
        }

        public static void Rows(TableLayoutPanel t, params (SizeType Type, float Value)[] rows)
        {
            t.RowStyles.Clear();
            foreach (var r in rows) t.RowStyles.Add(new RowStyle(r.Type, r.Value));
        }

        public static KWin.Controls.ThemedLabel Text(string text, Font font,
            KWin.Controls.TextRole role = KWin.Controls.TextRole.Primary, bool wrap = false) => new()
        {
            Text = text,
            Font = font,
            Role = role,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            TextAlign = wrap ? ContentAlignment.TopLeft : ContentAlignment.MiddleLeft,
            AutoEllipsis = !wrap
        };
    }
}
