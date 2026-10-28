using KWin.Themes;

namespace KWin.Controls
{
    /// <summary>
    /// ListView in the theme colors. The column header is drawn by K-Win (the Windows header stays
    /// light in dark mode); rows and check boxes are drawn by Windows. Clicking a column sorts by it.
    /// </summary>
    public class ThemedListView : ListView, IThemedControl
    {
        private ThemePalette _p = ThemeManager.Current;
        private int _sortColumn = -1;
        private bool _sortAscending = true;

        /// <summary>Column indexes that hold sizes in bytes (stored in item.SubItems[i].Tag as long) for numeric sorting.</summary>
        public HashSet<int> NumericColumns { get; } = new();

        public ThemedListView()
        {
            View = View.Details;
            FullRowSelect = true;
            OwnerDraw = true;
            BorderStyle = BorderStyle.None;
            HeaderStyle = ColumnHeaderStyle.Clickable;
            DoubleBuffered = true;
            Font = UiFonts.Regular(9.5f);
        }

        public void ApplyTheme(ThemePalette p)
        {
            _p = p;
            BackColor = p.Input;
            ForeColor = p.Text;
            Invalidate();
        }

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            using (var b = new SolidBrush(_p.Card))
                e.Graphics.FillRectangle(b, e.Bounds);
            using (var pen = new Pen(_p.CardBorder))
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

            string text = e.Header?.Text ?? "";
            if (e.ColumnIndex == _sortColumn) text += _sortAscending ? "  ▲" : "  ▼";
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
                        (RightToLeft == RightToLeft.Yes ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left);
            var r = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, UiFonts.Semibold(9f), r, _p.TextMuted, flags);
        }

        private bool _fitting;

        /// <summary>
        /// The last column fills the remaining width, so no unpainted (light) header area is left
        /// to the right of the columns.
        /// </summary>
        private void FitLastColumn()
        {
            if (_fitting || Columns.Count == 0 || !IsHandleCreated) return;
            _fitting = true;
            try { Columns[^1].Width = -2; }
            finally { _fitting = false; }
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); FitLastColumn(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); FitLastColumn(); }

        protected override void OnDrawItem(DrawListViewItemEventArgs e) => e.DrawDefault = true;
        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e) => e.DrawDefault = true;

        protected override void OnColumnClick(ColumnClickEventArgs e)
        {
            base.OnColumnClick(e);
            if (e.Column == _sortColumn) _sortAscending = !_sortAscending;
            else { _sortColumn = e.Column; _sortAscending = true; }
            ListViewItemSorter = new Sorter(_sortColumn, _sortAscending, NumericColumns.Contains(_sortColumn));
            Sort();
            Invalidate();
        }

        private sealed class Sorter : System.Collections.IComparer
        {
            private readonly int _col; private readonly bool _asc; private readonly bool _numeric;
            public Sorter(int col, bool asc, bool numeric) { _col = col; _asc = asc; _numeric = numeric; }

            public int Compare(object? x, object? y)
            {
                if (x is not ListViewItem a || y is not ListViewItem b) return 0;
                int r;
                if (_numeric)
                {
                    long va = a.SubItems.Count > _col && a.SubItems[_col].Tag is long la ? la : -1;
                    long vb = b.SubItems.Count > _col && b.SubItems[_col].Tag is long lb ? lb : -1;
                    r = va.CompareTo(vb);
                }
                else
                {
                    string ta = a.SubItems.Count > _col ? a.SubItems[_col].Text : "";
                    string tb = b.SubItems.Count > _col ? b.SubItems[_col].Text : "";
                    r = string.Compare(ta, tb, StringComparison.CurrentCultureIgnoreCase);
                }
                return _asc ? r : -r;
            }
        }
    }
}
