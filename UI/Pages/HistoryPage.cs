using KWin.Controls;
using KWin.Models;
using KWin.Services;
using KWin.Themes;
using KWin.Utils;
using static KWin.Utils.Loc;

namespace KWin.UI.Pages
{
    public sealed class HistoryActions
    {
        public required Func<IReadOnlyList<OperationRecord>> Load { get; init; }
        /// <summary>Whether the operation's undo entry still exists.</summary>
        public required Func<string?, bool> HasUndo { get; init; }
        /// <summary>(allowed, reason) - reason is "not_available" or the name of a later operation to undo first.</summary>
        public required Func<string?, (bool Allowed, string Reason)> CanUndo { get; init; }
        public required Func<OperationRecord, Task> Undo { get; init; }
    }

    /// <summary>
    /// Operation history: every operation K-win performed, with result, changed values
    /// (original -> new), errors, warnings, restore point and undo availability.
    /// Per-operation undo is offered only when it is technically possible.
    /// </summary>
    public sealed class HistoryPage : UserControl, IThemedControl
    {
        private readonly HistoryActions _a;
        private readonly ThemedListView _list = new() { Dock = DockStyle.Fill, MultiSelect = false };
        private readonly TextBox _details = new()
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true
        };
        private readonly AccentButton _undo;
        private readonly ThemedLabel _undoReason, _summary;
        private List<OperationRecord> _records = new();

        public HistoryPage(HistoryActions a)
        {
            _a = a;
            DoubleBuffered = true;

            var root = Ui.Grid(2, 1);
            Ui.Columns(root, (SizeType.Percent, 58), (SizeType.Percent, 42));

            var left = new RoundedPanel { Kind = SurfaceKind.Card, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0), Padding = new Padding(14) };
            var lgrid = Ui.Grid(1, 2);
            Ui.Rows(lgrid, (SizeType.Percent, 100), (SizeType.Absolute, 28));
            _list.Columns.Add(Get("hist.col_time"), 140);
            _list.Columns.Add(Get("hist.col_operation"), 260);
            _list.Columns.Add(Get("hist.col_result"), 100);
            _list.Columns.Add(Get("hist.col_undo"), 160);
            _list.Columns.Add(Get("hist.col_restore_point"), 150);
            _list.ShowItemToolTips = true;
            _list.SelectedIndexChanged += (s, e) => ShowSelected();
            lgrid.Controls.Add(_list, 0, 0);
            _summary = Ui.Text("", UiFonts.Regular(9f), TextRole.Muted);
            lgrid.Controls.Add(_summary, 0, 1);
            left.Controls.Add(lgrid);
            root.Controls.Add(left, 0, 0);

            var right = new RoundedPanel { Kind = SurfaceKind.Card, Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0), Padding = new Padding(14) };
            var rgrid = Ui.Grid(1, 3);
            Ui.Rows(rgrid, (SizeType.Percent, 100), (SizeType.Absolute, 44), (SizeType.Absolute, 48));
            _details.Font = UiFonts.Regular(10f);
            rgrid.Controls.Add(_details, 0, 0);
            _undo = new AccentButton { Text = Get("hist.undo_this"), FontSize = 10f, Dock = DockStyle.Left, Width = 230, Margin = new Padding(0, 6, 0, 2), Enabled = false };
            _undo.Click += async (s, e) =>
            {
                if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not OperationRecord r) return;
                await _a.Undo(r);
                Reload();
            };
            rgrid.Controls.Add(_undo, 0, 1);
            _undoReason = Ui.Text("", UiFonts.Regular(9f), TextRole.Muted, wrap: true);
            rgrid.Controls.Add(_undoReason, 0, 2);
            right.Controls.Add(rgrid);
            root.Controls.Add(right, 1, 0);

            Controls.Add(root);
            VisibleChanged += (s, e) => { if (Visible) Reload(); };
        }

        public void Reload()
        {
            string? selectedId = _list.SelectedItems.Count > 0 ? (_list.SelectedItems[0].Tag as OperationRecord)?.Id : null;
            _records = _a.Load().ToList();
            var p = ThemeManager.Current;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var r in _records)
            {
                var item = new ListViewItem((r.CompletedUtc ?? r.StartedUtc).ToLocalTime().ToString("g", Loc.Culture)) { Tag = r };
                item.SubItems.Add(r.Name);
                item.SubItems.Add(StatusText(r.Status));
                item.SubItems.Add(UndoText(r));
                item.SubItems.Add(RestorePointText(r.RestorePoint));
                item.ToolTipText = r.Name;
                item.ForeColor = r.Status == OperationStatus.Failed ? p.Error
                    : r.UndoneUtc != null || r.Status is OperationStatus.Cancelled or OperationStatus.Skipped ? p.TextMuted
                    : r.Warnings.Count > 0 ? p.Warning : p.Text;
                _list.Items.Add(item);
                if (r.Id == selectedId) item.Selected = true;
            }
            _list.EndUpdate();
            _summary.Text = _records.Count == 0 ? Get("dash.no_actions") : Get("hist.page_summary", _records.Count);
            if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0) _list.Items[0].Selected = true;
            ShowSelected();
        }

        public static string StatusText(OperationStatus s) => s switch
        {
            OperationStatus.Completed => Get("hist.status_completed"),
            OperationStatus.Failed => Get("hist.status_failed"),
            OperationStatus.Cancelled => Get("hist.status_cancelled"),
            OperationStatus.Skipped => Get("hist.status_skipped"),
            OperationStatus.RestartRequired => Get("hist.status_restart"),
            _ => s.ToString()
        };

        private string UndoText(OperationRecord r)
        {
            if (r.UndoneUtc != null) return Get("hist.undone");
            if (r.UndoId == null) return Get("hist.rev_none");
            if (!_a.HasUndo(r.UndoId)) return Get("hist.undo_gone");
            return r.Reversibility == Reversibility.Full ? Get("hist.rev_full") : Get("hist.rev_partial");
        }

        private static string RestorePointText(RestorePointStatus s) => s switch
        {
            RestorePointStatus.Created => Get("hist.rp_created"),
            RestorePointStatus.SkippedRecentExists => Get("hist.rp_recent"),
            RestorePointStatus.SkippedNotAdmin => Get("hist.rp_not_admin"),
            RestorePointStatus.Failed => Get("hist.rp_failed"),
            _ => "—"
        };

        private static string Value(RegistryValueSnapshot v) =>
            v.Exists ? Loc.Ltr(string.IsNullOrEmpty(v.Data) ? "\"\"" : v.Data!) : Get("hist.not_set");

        private void ShowSelected()
        {
            if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not OperationRecord r)
            {
                _details.Text = Get("hist.select_hint");
                _undo.Enabled = false;
                _undoReason.Text = "";
                return;
            }

            var nl = Environment.NewLine;
            var sb = new System.Text.StringBuilder();
            sb.Append(r.Name).Append(nl);
            sb.Append((r.CompletedUtc ?? r.StartedUtc).ToLocalTime().ToString("F", Loc.Culture)).Append(nl).Append(nl);
            sb.Append(Get("hist.col_result")).Append(": ").Append(StatusText(r.Status)).Append(nl);
            sb.Append(Get("hist.col_restore_point")).Append(": ").Append(RestorePointText(r.RestorePoint)).Append(nl);
            sb.Append(Get("hist.col_undo")).Append(": ").Append(UndoText(r)).Append(" — ").Append(r.UndoneUtc != null
                ? Get("hist.undone_at", r.UndoneUtc.Value.ToLocalTime().ToString("g", Loc.Culture))
                : r.UndoId == null ? Get("hist.explain_none")
                : r.Reversibility == Reversibility.Full ? Get("hist.explain_full") : Get("hist.explain_partial")).Append(nl);

            if (!string.IsNullOrWhiteSpace(r.Error))
                sb.Append(nl).Append(Get("hist.errors")).Append(nl).Append(r.Error.Replace(" | ", nl)).Append(nl);
            if (r.Warnings.Count > 0)
                sb.Append(nl).Append(Get("hist.warnings")).Append(nl).Append(string.Join(nl, r.Warnings)).Append(nl);

            if (r.RegistryChanges.Count > 0)
            {
                sb.Append(nl).Append(Get("hist.changes", r.RegistryChanges.Count)).Append(nl);
                foreach (var c in r.RegistryChanges)
                {
                    if (c.Operation == RegistryOperation.DeleteKeyTree)
                        sb.Append("• ").Append(Loc.Ltr(c.KeyPath)).Append(": ").Append(Get("hist.key_removed")).Append(nl);
                    else
                        sb.Append("• ").Append(Loc.Ltr($"{c.KeyPath}\\{(string.IsNullOrEmpty(c.ValueName) ? "(Default)" : c.ValueName)}"))
                          .Append(nl).Append("    ").Append(Value(c.Before)).Append("  →  ").Append(Value(c.After)).Append(nl);
                }
            }
            if (!string.IsNullOrWhiteSpace(r.Details))
                sb.Append(nl).Append(Get("hist.col_details")).Append(": ").Append(r.Details).Append(nl);
            _details.Text = sb.ToString();

            var (allowed, reason) = r.UndoneUtc == null && r.UndoId != null ? _a.CanUndo(r.UndoId) : (false, "not_available");
            _undo.Enabled = allowed;
            _undoReason.Text = allowed
                ? (r.Reversibility == Reversibility.Full ? Get("hist.undo_ready_full") : Get("hist.undo_ready_partial"))
                : r.UndoneUtc != null ? Get("hist.undone_at", r.UndoneUtc.Value.ToLocalTime().ToString("g", Loc.Culture))
                : r.UndoId == null ? Get("hist.explain_none")
                : reason == "not_available" ? Get("hist.undo_gone_explain")
                : Get("hist.undo_blocked", reason);
        }

        public void ApplyTheme(ThemePalette p)
        {
            BackColor = p.WindowBackground;
            _details.BackColor = p.Card;
            _details.ForeColor = p.Text;
        }
    }
}
