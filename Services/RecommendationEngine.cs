using KWin.Models;
using KWin.Utils;

namespace KWin.Services
{
    /// <summary>One proposed change: a setting that is not in K-Win's recommended state.</summary>
    public sealed class Recommendation
    {
        public required SettingDefinition Setting { get; init; }
        public required SettingState Current { get; init; }
        /// <summary>The state K-Win recommends (true = On).</summary>
        public required bool TargetOn { get; init; }
        public RiskLevel Risk => Setting.Risk;
        /// <summary>Another tool changed this setting: shown, but never pre-selected.</summary>
        public bool ChangedOutsideKWin => Current == SettingState.Custom;
        public bool Selected { get; set; }
    }

    public enum SkipReason { NotApplicable, LockedByOrganization, LockedByLocalPolicy, StateUnknown }

    public sealed class AnalysisResult
    {
        public List<Recommendation> Recommendations { get; } = new();
        /// <summary>Settings already in the recommended state.</summary>
        public List<SettingDefinition> AlreadyRecommended { get; } = new();
        public List<(SettingDefinition Setting, SkipReason Reason)> Skipped { get; } = new();
        /// <summary>Personal preferences (no recommendation) - never offered as optimizations.</summary>
        public List<SettingDefinition> Preferences { get; } = new();
    }

    public enum ItemOutcome { Applied, Failed, NotRun }

    public sealed class ItemResult
    {
        public required Recommendation Recommendation { get; init; }
        public ItemOutcome Outcome { get; set; } = ItemOutcome.NotRun;
        public string? Error { get; set; }
        /// <summary>Non-registry step failed after the registry change succeeded.</summary>
        public string? Warning { get; set; }
    }

    /// <summary>
    /// Builds the Optimize Now proposal from the settings catalog and the real current state.
    /// Rules:
    ///  - only settings with a K-Win recommendation (preferences are never "optimizations");
    ///  - skipped when not applicable to this Windows build, locked by policy, or unreadable;
    ///  - pre-selected only when low risk and not changed by another tool;
    ///  - high-risk items are never pre-selected.
    /// </summary>
    public static class RecommendationEngine
    {
        public static AnalysisResult Analyze(
            IEnumerable<SettingDefinition> catalog,
            Func<SettingDefinition, SettingState> read,
            Func<SettingDefinition, LockReason> lockCheck,
            int windowsBuild)
        {
            var result = new AnalysisResult();
            foreach (var d in catalog)
            {
                if (d.RecommendOn is not bool target)
                {
                    result.Preferences.Add(d);
                    continue;
                }
                if (d.MinBuild > 0 && windowsBuild < d.MinBuild)
                {
                    result.Skipped.Add((d, SkipReason.NotApplicable));
                    continue;
                }
                var lockReason = lockCheck(d);
                if (lockReason != LockReason.None)
                {
                    result.Skipped.Add((d, lockReason == LockReason.Organization ? SkipReason.LockedByOrganization : SkipReason.LockedByLocalPolicy));
                    continue;
                }

                var state = read(d);
                if (state == SettingState.Unknown)
                {
                    result.Skipped.Add((d, SkipReason.StateUnknown));
                    continue;
                }
                if (state == (target ? SettingState.On : SettingState.Off))
                {
                    result.AlreadyRecommended.Add(d);
                    continue;
                }

                result.Recommendations.Add(new Recommendation
                {
                    Setting = d,
                    Current = state,
                    TargetOn = target,
                    Selected = DefaultSelected(d.Risk, state)
                });
            }

            result.Recommendations.Sort((a, b) => a.Risk.CompareTo(b.Risk));
            return result;
        }

        /// <summary>Only low-risk, untouched settings are pre-selected.</summary>
        public static bool DefaultSelected(RiskLevel risk, SettingState current) =>
            risk == RiskLevel.Low && current != SettingState.Custom;

        /// <summary>
        /// Applies the selected recommendations one by one. A failure does not stop the others;
        /// every item gets its own result.
        /// </summary>
        public static async Task<List<ItemResult>> ApplyAsync(
            IEnumerable<Recommendation> selected,
            Func<Recommendation, Task<(bool Ok, string? Error, string? Warning)>> applyOne,
            CancellationToken ct = default)
        {
            var results = new List<ItemResult>();
            foreach (var rec in selected.Where(r => r.Selected))
            {
                var item = new ItemResult { Recommendation = rec };
                results.Add(item);
                if (ct.IsCancellationRequested) continue; // stays NotRun
                try
                {
                    var (ok, error, warning) = await applyOne(rec);
                    item.Outcome = ok ? ItemOutcome.Applied : ItemOutcome.Failed;
                    item.Error = error;
                    item.Warning = warning;
                }
                catch (Exception ex)
                {
                    item.Outcome = ItemOutcome.Failed;
                    item.Error = ex.Message;
                    Logger.Instance.Error("OptimizeNow", rec.Setting.Id, ex);
                }
            }
            return results;
        }
    }
}
