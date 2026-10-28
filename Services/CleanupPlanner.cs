using KWin.Core;
using KWin.Models;
using KWin.Utils;

namespace KWin.Services
{
    public enum CleanupKind { Files, RecycleBin }

    /// <summary>One selectable cleanup category (a group of safe folders, or the Recycle Bin).</summary>
    public sealed class CleanupCategory
    {
        public required string Id { get; init; }
        /// <summary>Display name (browser name, or a localized category name).</summary>
        public required string Name { get; init; }
        public required string Description { get; init; }
        public CleanupKind Kind { get; init; } = CleanupKind.Files;
        public IReadOnlyList<CleanupTarget> Targets { get; init; } = Array.Empty<CleanupTarget>();
        public bool RequiresAdmin { get; init; }
        public bool Selected { get; set; }
        /// <summary>Measured by the last scan; null = not scanned or not measurable.</summary>
        public long? MeasuredBytes { get; set; }
        public long? MeasuredItems { get; set; }
    }

    public enum CategoryOutcome { Completed, Failed, Cancelled, NotRun }

    public sealed class CategoryResult
    {
        public required CleanupCategory Category { get; init; }
        public CategoryOutcome Outcome { get; set; } = CategoryOutcome.NotRun;
        public CleanupReport Report { get; } = new();
        public string? Error { get; set; }
    }

    /// <summary>
    /// Scan -> select -> clean workflow on top of the Phase 2 safe cleanup engine
    /// (<see cref="CleanupManager.CleanTargets(IEnumerable{CleanupTarget}, CleanupReport, IProgress{string}?, CancellationToken)"/>):
    /// links are never followed, protected folders are refused, files younger than 24 h in temp
    /// folders are kept. File deletion cannot be undone.
    /// </summary>
    public sealed class CleanupPlanner
    {
        private readonly Func<Result<(long SizeBytes, long ItemCount)>> _queryRecycleBin;
        private readonly Func<Result<bool>> _emptyRecycleBin;

        /// <param name="queryRecycleBin">Injected for tests; defaults to the real Recycle Bin query.</param>
        /// <param name="emptyRecycleBin">Injected for tests; defaults to emptying the real Recycle Bin.</param>
        public CleanupPlanner(
            Func<Result<(long, long)>>? queryRecycleBin = null,
            Func<Result<bool>>? emptyRecycleBin = null)
        {
            _queryRecycleBin = queryRecycleBin ?? CleanupManager.GetRecycleBinSize;
            _emptyRecycleBin = emptyRecycleBin ?? (() => new CleanupManager(new SystemTools()).EmptyRecycleBin());
        }

        /// <summary>The categories K-Win offers on this PC, with safe default selections.</summary>
        public static List<CleanupCategory> DefaultCategories(Func<string, string> text)
        {
            var list = new List<CleanupCategory>();
            var temp = CleanupManager.GetTempTargets();

            void AddTemp(string id, string label, bool admin)
            {
                var targets = temp.Where(t => t.Label == label).ToList();
                if (targets.Count == 0 && !admin) return;
                list.Add(new CleanupCategory
                {
                    Id = id,
                    Name = text($"cleanup.cat.{id}"),
                    Description = text($"cleanup.cat.{id}_desc"),
                    Targets = targets,
                    RequiresAdmin = admin,
                    Selected = targets.Count > 0
                });
            }

            AddTemp("user_temp", "User Temp", false);
            AddTemp("error_reports", "Error Reports", false);
            AddTemp("windows_temp", "Windows Temp", true); // only has targets when running as administrator

            foreach (var browser in CleanupManager.GetBrowserTargets().GroupBy(t => t.Label))
            {
                list.Add(new CleanupCategory
                {
                    Id = "browser:" + browser.Key,
                    Name = text("cleanup.cat.browser_prefix") + " " + browser.Key,
                    Description = text("cleanup.cat.browser_desc"),
                    Targets = browser.ToList(),
                    Selected = true
                });
            }

            list.Add(new CleanupCategory
            {
                Id = "recycle_bin",
                Name = text("cleanup.cat.recycle_bin"),
                Description = text("cleanup.cat.recycle_bin_desc"),
                Kind = CleanupKind.RecycleBin,
                Selected = false // your deleted files: never selected by default
            });
            return list;
        }

        /// <summary>Measures each category. Measuring never deletes anything.</summary>
        public Task ScanAsync(IReadOnlyList<CleanupCategory> categories, IProgress<int>? progress, CancellationToken ct) =>
            Task.Run(() =>
            {
                for (int i = 0; i < categories.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var c = categories[i];
                    if (c.Kind == CleanupKind.RecycleBin)
                    {
                        var r = _queryRecycleBin();
                        c.MeasuredBytes = r.Success ? r.Data.SizeBytes : null;
                        c.MeasuredItems = r.Success ? r.Data.ItemCount : null;
                    }
                    else if (c.Targets.Count > 0)
                    {
                        c.MeasuredBytes = CleanupManager.Measure(c.Targets).TotalBytes;
                    }
                    else
                    {
                        c.MeasuredBytes = null; // e.g. Windows Temp without admin rights
                    }
                    progress?.Report(i + 1);
                }
            }, ct);

        /// <summary>
        /// Cleans the selected categories in order. Stops at cancellation and keeps the partial
        /// results (what was already deleted is reported). Unselected categories are never touched.
        /// </summary>
        public Task<List<CategoryResult>> ExecuteAsync(IReadOnlyList<CleanupCategory> categories,
            IProgress<string>? progress, CancellationToken ct) =>
            Task.Run(() =>
            {
                var results = categories.Where(c => c.Selected).Select(c => new CategoryResult { Category = c }).ToList();
                foreach (var res in results)
                {
                    if (ct.IsCancellationRequested) { res.Outcome = CategoryOutcome.NotRun; continue; }
                    progress?.Report(res.Category.Name);
                    try
                    {
                        if (res.Category.Kind == CleanupKind.RecycleBin)
                        {
                            var r = _emptyRecycleBin();
                            res.Outcome = r.Success ? CategoryOutcome.Completed : CategoryOutcome.Failed;
                            res.Error = r.ErrorMessage;
                            if (r.Success) res.Report.BytesFreed = res.Category.MeasuredBytes ?? 0;
                        }
                        else
                        {
                            CleanupManager.CleanTargets(res.Category.Targets, res.Report, progress, ct);
                            res.Outcome = CategoryOutcome.Completed;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        res.Outcome = CategoryOutcome.Cancelled; // partial counts stay in res.Report
                    }
                    catch (Exception ex)
                    {
                        res.Outcome = CategoryOutcome.Failed;
                        res.Error = ex.Message;
                        Logger.Instance.Error("CleanupPlanner", res.Category.Id, ex);
                    }
                }
                return results;
            });
    }
}
