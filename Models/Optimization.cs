namespace KWin.Models
{
    /// <summary>
    /// Risk level classification for optimizations.
    /// Determines the level of warning shown to the user.
    /// </summary>
    public enum RiskLevel
    {
        /// <summary>Safe, easily reversible changes like visual effects.</summary>
        Low,

        /// <summary>Moderate changes like disabling services; reversible but may affect functionality.</summary>
        Medium,

        /// <summary>Significant changes like clearing data; may be partially irreversible.</summary>
        High
    }

    /// <summary>
    /// Category of optimization for tab organization.
    /// </summary>
    public enum OptimizationCategory
    {
        Performance,
        Privacy,
        Cleanup
    }

    /// <summary>
    /// Represents a single optimization that can be applied to the system.
    /// Contains all metadata needed for UI display and execution.
    /// </summary>
    public class Optimization
    {
        /// <summary>Unique identifier for this optimization.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Display name shown in the UI.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Detailed description of what this optimization does.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Risk classification for user warning.</summary>
        public RiskLevel Risk { get; set; }

        /// <summary>Which tab this optimization belongs to.</summary>
        public OptimizationCategory Category { get; set; }

        /// <summary>Whether this optimization is currently selected by the user.</summary>
        public bool IsSelected { get; set; }

        /// <summary>Whether this optimization is currently applied on the system.</summary>
        public bool IsCurrentlyApplied { get; set; }

        /// <summary>Whether this optimization can be undone.</summary>
        public bool IsReversible { get; set; } = true;

        /// <summary>List of specific changes this optimization will make (for preview dialog).</summary>
        public List<string> ChangeDetails { get; set; } = new();

        /// <summary>Minimum Windows 11 build required (0 = any Windows 11).</summary>
        public int MinBuild { get; set; } = 22000;

        /// <summary>Additional warning text shown before applying.</summary>
        public string? WarningText { get; set; }
    }
}
