using KWin.Core;
using KWin.Models;
using KWin.Services;
using KWin.Utils;
using static KWin.Utils.Loc;

namespace KWin.UI
{
    /// <summary>
    /// Optimize Now: analyze -> recommendations by risk -> choose -> full preview -> confirm ->
    /// one restore point -> apply each (verified) -> summary -> undo in one step.
    /// </summary>
    public partial class MainForm
    {
        private PolicyLocks? _policyLocks;
        private PolicyLocks Locks => _policyLocks ??= new PolicyLocks();

        private string UndoTextFor(SettingDefinition d) =>
            d.Id is "telemetry" or "sysmain" or "fast_startup" ? Get("card.undo_mixed") : Get("card.undo_full");

        private async Task OptimizeNowAsync()
        {
            if (DevMode.IsUiPreview)
            {
                // The preview may analyze and show recommendations; applying is blocked later.
                Logger.Instance.Info("OptimizeNow", "preview", "analysis_only");
            }

            SetBusy(true, Get("opt.analyzing"));
            AnalysisResult analysis;
            try
            {
                analysis = await Task.Run(() => RecommendationEngine.Analyze(
                    SettingCatalog.All, _tweaks.ReadState, Locks.Check, WindowsVersionChecker.CurrentBuild));
            }
            finally
            {
                SetBusy(false);
            }

            List<Recommendation> chosen;
            using (var dialog = OptimizeDialog.ForSelection(analysis, UndoTextFor))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                chosen = dialog.Selected.ToList();
            }
            if (chosen.Count == 0) return;

            // Complete preview of every change, then explicit confirmation.
            var lines = new List<string> { Get("opt.preview_intro", chosen.Count), "" };
            foreach (var r in chosen)
            {
                lines.Add($"■ {Get(r.Setting.TextKey("title"))}: {Get(r.Setting.TextKey(r.TargetOn ? "on" : "off"))}  ({OptimizeDialog.RiskText(r.Risk)})");
                lines.AddRange(TweakEngine.DescribeChanges(r.Setting, r.TargetOn).Select(l => "    " + l));
                if (HookDescription(r.Setting, r.TargetOn) is string hook) lines.Add("    " + hook);
                lines.Add($"    {Get("card.side_effects")}: {Get(r.Setting.TextKey("side"))}");
            }
            lines.Add("");
            lines.Add(Get("opt.preview_rp"));
            lines.Add(Get("card.preview_undo"));
            bool anyMedium = chosen.Any(r => r.Risk != RiskLevel.Low);
            if (!PreviewDialog.ShowPreview(this, Get("opt.title"), lines, anyMedium ? Get("opt.preview_medium_warning") : null))
                return;

            bool needsAdmin = chosen.Any(r => r.Setting.RequiresAdmin);
            List<ItemResult>? results = null;
            RestorePointStatus rpStatus = RestorePointStatus.NotRequested;

            await ExecuteWithSafetyAsync(Get("opt.op_name", chosen.Count), async () =>
            {
                var undoSteps = new List<Func<Task<Result<bool>>>>();
                results = await RecommendationEngine.ApplyAsync(chosen, async rec =>
                {
                    var r = _tweaks.Apply(rec.Setting, rec.TargetOn);
                    if (!r.Success) return (false, r.ErrorMessage, null);
                    var (hookError, undo) = await RunSettingHookAsync(rec.Setting, rec.TargetOn);
                    if (undo != null) undoSteps.Add(undo);
                    return (true, null, hookError);
                });

                // One undo entry for the whole run: registry changes are attached automatically;
                // non-registry steps run afterwards, newest first.
                undoSteps.Reverse();
                _safety.PushUndoAction(new UndoAction
                {
                    Description = Get("opt.op_name", chosen.Count),
                    UndoDelegate = undoSteps.Count == 0 ? null : async () =>
                    {
                        var errors = new List<string>();
                        foreach (var step in undoSteps)
                        {
                            var u = await step();
                            if (!u.Success) errors.Add(u.ErrorMessage ?? "");
                        }
                        return errors.Count == 0 ? Result<bool>.Ok(true) : Result<bool>.Fail(string.Join("\n", errors));
                    }
                });

                int failed = results.Count(x => x.Outcome == ItemOutcome.Failed);
                foreach (var w in results.Where(x => x.Warning != null))
                    _currentWarnings.Add($"{Get(w.Recommendation.Setting.TextKey("title"))}: {w.Warning}");
                SetStatus(Get("opt.status", results.Count - failed, failed), failed > 0);
            }, requiresAdmin: needsAdmin, onRecorded: rec => rpStatus = rec.RestorePoint);

            if (results == null) return; // cancelled, blocked or not elevated

            string rpText = rpStatus switch
            {
                RestorePointStatus.Created => Get("opt.rp_created"),
                RestorePointStatus.SkippedRecentExists => Get("opt.rp_recent"),
                RestorePointStatus.SkippedNotAdmin => Get("opt.rp_not_admin"),
                RestorePointStatus.Failed => Get("opt.rp_failed"),
                _ => Get("opt.rp_off")
            };
            bool restart = results.Any(r => r.Outcome == ItemOutcome.Applied && (r.Recommendation.Setting.RestartRequired || r.Recommendation.Setting.SignOutRequired));
            using var summary = OptimizeDialog.ForSummary(results, rpText, restart, UndoTextFor);
            summary.ShowDialog(this);
        }
    }
}
