namespace KWin.Utils
{
    /// <summary>
    /// Development-only UI preview, used to look at the interface on a non-Windows 11 PC.
    ///
    /// - Exists only in DEBUG builds. In Release builds IsUiPreview is always false and the
    ///   command-line switch is ignored, so it can never be enabled in a published K-Win.
    /// - Enabled only with the explicit switch "--dev-ui-preview".
    /// - While enabled, K-Win is READ-ONLY: every system-changing code path refuses to run
    ///   (registry writes, services, tools, cleanup, uninstall, restore points, elevation).
    /// </summary>
    public static class DevMode
    {
        public const string Switch = "--dev-ui-preview";

#if DEBUG
        public static bool IsUiPreview { get; private set; }

        public static void Initialize(string[] args)
        {
            IsUiPreview = args.Any(a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase));
        }
#else
        public static readonly bool IsUiPreview = false;

        public static void Initialize(string[] args) { /* not available in Release builds */ }
#endif

        public const string BlockedMessage =
            "Development UI preview is read-only: this action is disabled and no system change was made.";

        /// <summary>Throws when the development preview is active. Call at the start of any system change.</summary>
        public static void ThrowIfReadOnly(string action)
        {
            if (IsUiPreview)
                throw new InvalidOperationException($"{BlockedMessage} ({action})");
        }
    }
}
