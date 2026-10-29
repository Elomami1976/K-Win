using Microsoft.Win32;

namespace KWin.Models
{
    public enum SettingGroup { Performance, Privacy, Tweaks, Memory }

    /// <summary>Current state of a setting, read from the registry.</summary>
    public enum SettingState
    {
        /// <summary>Matches the "On" values.</summary>
        On,
        /// <summary>Matches the "Off" values (usually the Windows default).</summary>
        Off,
        /// <summary>Some other configuration (changed by another tool or partly applied).</summary>
        Custom,
        /// <summary>Could not be read.</summary>
        Unknown
    }

    /// <summary>
    /// One registry value a setting writes.
    /// Value == null means "the value must not exist" (deleted = Windows default / not configured).
    /// DeleteKeyTree means "the key must not exist".
    /// MatchesWhenMissing: a missing value also counts as this state (Windows' built-in default).
    /// </summary>
    public sealed record RegistryTweak(
        string Key,
        string ValueName,
        object? Value,
        RegistryValueKind Kind = RegistryValueKind.DWord,
        bool DeleteKeyTree = false,
        bool MatchesWhenMissing = false);

    /// <summary>
    /// A two-state Windows setting described as data: the registry values for "On" and "Off".
    /// Text comes from localization keys "setting.{Id}.title|desc|benefit|side|on|off".
    /// </summary>
    public sealed class SettingDefinition
    {
        public required string Id { get; init; }
        public required SettingGroup Group { get; init; }
        public string Glyph { get; init; } = "";
        public required IReadOnlyList<RegistryTweak> On { get; init; }
        public required IReadOnlyList<RegistryTweak> Off { get; init; }

        /// <summary>true = K-Win recommends On, false = recommends Off, null = personal preference.</summary>
        public bool? RecommendOn { get; init; }

        /// <summary>
        /// Risk of applying the recommended state: Low = easy to live with and fully reversible;
        /// Medium = noticeable side effects or a non-registry step; High = never pre-selected.
        /// </summary>
        public RiskLevel Risk { get; init; } = RiskLevel.Low;

        /// <summary>Change takes effect after restart (true) or sign-out (see SignOutRequired).</summary>
        public bool RestartRequired { get; init; }
        public bool SignOutRequired { get; init; }

        /// <summary>Minimum Windows build where the setting has any effect (0 = all).</summary>
        public int MinBuild { get; init; }

        /// <summary>Writes Group Policy values (HKLM\SOFTWARE\Policies or HKCU\...\Policies).</summary>
        public bool IsPolicy => On.Concat(Off).Any(t => t.Key.Contains(@"\Policies\", StringComparison.OrdinalIgnoreCase));

        /// <summary>Writes machine-wide values (HKLM) and therefore needs administrator rights.</summary>
        public bool RequiresAdmin => On.Concat(Off).Any(t =>
            t.Key.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase) ||
            t.Key.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase));

        public string TextKey(string part) => $"setting.{Id}.{part}";
    }
}
