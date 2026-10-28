using KWin.Core;
using KWin.Models;
using KWin.Utils;
using Microsoft.Win32;

namespace KWin.Services
{
    /// <summary>
    /// Reads and applies <see cref="SettingDefinition"/>s through <see cref="RegistryHelper"/>, so every
    /// write is backed up, verified, recorded for undo and reported on failure. After applying, the
    /// state is read back and the result fails if Windows does not show the expected state.
    /// </summary>
    public sealed class TweakEngine
    {
        private readonly RegistryHelper _registry;
        private readonly Func<string, string> _mapKey;

        /// <param name="mapKey">Rewrites key paths (tests redirect them into a throwaway HKCU key).</param>
        public TweakEngine(RegistryHelper registry, Func<string, string>? mapKey = null)
        {
            _registry = registry;
            _mapKey = mapKey ?? (k => k);
        }

        public SettingState ReadState(SettingDefinition d)
        {
            try
            {
                bool on = Matches(d.On);
                bool off = Matches(d.Off);
                if (on) return SettingState.On;
                if (off) return SettingState.Off;
                return SettingState.Custom;
            }
            catch
            {
                return SettingState.Unknown;
            }
        }

        private bool Matches(IReadOnlyList<RegistryTweak> tweaks)
        {
            foreach (var t in tweaks)
            {
                string key = _mapKey(t.Key);
                if (t.DeleteKeyTree)
                {
                    var exists = _registry.KeyExists(key);
                    if (!exists.Success) throw new InvalidOperationException(exists.ErrorMessage);
                    if (exists.Data) return false;
                    continue;
                }

                var snap = _registry.ReadSnapshot(key, t.ValueName);
                if (!snap.Success) throw new InvalidOperationException(snap.ErrorMessage);
                var current = snap.Data.Value;

                if (t.Value == null)
                {
                    if (current.Exists) return false;
                    continue;
                }
                if (!current.Exists)
                {
                    if (t.MatchesWhenMissing) continue;
                    return false;
                }
                var expected = RegistryValueCodec.Encode(t.Value, RegistryValueCodec.WritableKind(t.Kind));
                if (current.Kind != RegistryValueCodec.WritableKind(t.Kind) || current.Data != expected)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Writes the "On" or "Off" values. Every write is verified individually by RegistryHelper,
        /// then the whole setting is read back. Returns the number of registry changes made.
        /// </summary>
        public Result<int> Apply(SettingDefinition d, bool on)
        {
            DevMode.ThrowIfReadOnly($"setting {d.Id}");
            int changes = 0;
            var errors = new List<string>();

            foreach (var t in on ? d.On : d.Off)
            {
                string key = _mapKey(t.Key);
                Result<string> r;
                if (t.DeleteKeyTree)
                    r = _registry.DeleteKeyTreeWithBackup(key);
                else if (t.Value == null)
                    r = _registry.DeleteValueWithBackup(key, t.ValueName);
                else
                {
                    // Skip values that already have the wanted data: no needless writes or undo entries.
                    var snap = _registry.ReadSnapshot(key, t.ValueName);
                    if (snap.Success && snap.Data.Value.Exists &&
                        snap.Data.Value.Kind == RegistryValueCodec.WritableKind(t.Kind) &&
                        snap.Data.Value.Data == RegistryValueCodec.Encode(t.Value, RegistryValueCodec.WritableKind(t.Kind)))
                        continue;
                    r = _registry.SetValueWithBackup(key, t.ValueName, t.Value, t.Kind);
                }

                if (r.Success) changes++;
                else errors.Add($"{key}\\{t.ValueName}: {r.ErrorMessage}");
            }

            if (errors.Count > 0)
                return Result<int>.Fail(string.Join("\n", errors));

            var state = ReadState(d);
            var expected = on ? SettingState.On : SettingState.Off;
            if (state != expected)
                return Result<int>.Fail($"Verification failed: Windows reports '{state}' instead of '{expected}'.");

            return Result<int>.Ok(changes);
        }

        /// <summary>Human-readable list of the registry changes, for the preview dialog.</summary>
        public static IReadOnlyList<string> DescribeChanges(SettingDefinition d, bool on)
        {
            var lines = new List<string>();
            foreach (var t in on ? d.On : d.Off)
            {
                string name = string.IsNullOrEmpty(t.ValueName) ? "(Default)" : t.ValueName;
                if (t.DeleteKeyTree) lines.Add($"Registry: remove key {t.Key}");
                else if (t.Value == null) lines.Add($"Registry: remove {t.Key}\\{name} (Windows default)");
                else lines.Add($"Registry: {t.Key}\\{name} = {FormatValue(t.Value, t.Kind)}");
            }
            return lines;
        }

        private static string FormatValue(object value, RegistryValueKind kind) =>
            kind is RegistryValueKind.String or RegistryValueKind.ExpandString ? $"\"{value}\"" : Convert.ToString(value) ?? "";

        /// <summary>
        /// Checks a definition for mistakes: invalid or blocked paths, On/Off touching different values,
        /// wrong value types. Used by the tests on the whole catalog.
        /// </summary>
        public static IReadOnlyList<string> Validate(SettingDefinition d)
        {
            var problems = new List<string>();
            if (d.On.Count == 0 || d.Off.Count == 0) problems.Add($"{d.Id}: needs On and Off values");

            foreach (var t in d.On.Concat(d.Off))
            {
                if (!Validator.IsValidRegistryPath(t.Key, out var err))
                    problems.Add($"{d.Id}: {t.Key}: {err}");
                if (t.Value != null && t.Kind == RegistryValueKind.DWord && t.Value is not int)
                    problems.Add($"{d.Id}: {t.ValueName} DWORD value must be an int");
                if (t.Value != null && t.Kind == RegistryValueKind.String && t.Value is not string)
                    problems.Add($"{d.Id}: {t.ValueName} string value must be a string");
                if (t.DeleteKeyTree && t.Value != null)
                    problems.Add($"{d.Id}: key deletion cannot also set a value");
            }

            static string Id(RegistryTweak t) => t.DeleteKeyTree ? t.Key.ToUpperInvariant() : $"{t.Key}\\{t.ValueName}".ToUpperInvariant();
            var on = d.On.Select(Id).ToHashSet();
            var off = d.Off.Select(Id).ToHashSet();
            // Off may delete a parent key instead of the individual values (classic context menu).
            bool keyTreeOff = d.Off.Any(t => t.DeleteKeyTree);
            if (!keyTreeOff && !on.SetEquals(off))
                problems.Add($"{d.Id}: On and Off must set the same values");
            if (on.Count != d.On.Count || off.Count != d.Off.Count)
                problems.Add($"{d.Id}: duplicate values");

            return problems;
        }
    }
}
