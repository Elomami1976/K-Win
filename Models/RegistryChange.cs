using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace KWin.Models
{
    /// <summary>What kind of registry write a change record describes.</summary>
    public enum RegistryOperation
    {
        SetValue,
        DeleteValue,
        DeleteKeyTree
    }

    /// <summary>
    /// The state of one registry value at a point in time, in a form that can be saved to JSON.
    /// </summary>
    public class RegistryValueSnapshot
    {
        [JsonPropertyName("exists")]
        public bool Exists { get; set; }

        [JsonPropertyName("kind")]
        public RegistryValueKind Kind { get; set; } = RegistryValueKind.Unknown;

        /// <summary>The value encoded by <see cref="RegistryValueCodec"/>.</summary>
        [JsonPropertyName("data")]
        public string? Data { get; set; }

        public static RegistryValueSnapshot Missing() => new() { Exists = false };

        public bool SameAs(RegistryValueSnapshot other) =>
            Exists == other.Exists &&
            (!Exists || (Kind == other.Kind && string.Equals(Data, other.Data, StringComparison.Ordinal)));

        public override string ToString() => Exists ? $"{Kind}:{Data}" : "(not set)";
    }

    /// <summary>
    /// A single, precisely reversible registry change. Saved to disk so undo survives restarts.
    /// </summary>
    public class RegistryChange
    {
        [JsonPropertyName("op")]
        public RegistryOperation Operation { get; set; } = RegistryOperation.SetValue;

        /// <summary>Full key path, e.g. HKCU\Software\Example.</summary>
        [JsonPropertyName("key")]
        public string KeyPath { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public string ValueName { get; set; } = string.Empty;

        /// <summary>Whether the key existed before the change.</summary>
        [JsonPropertyName("key_existed")]
        public bool KeyExisted { get; set; }

        /// <summary>
        /// The highest key K-Win had to create for this change, or null if none were created.
        /// Undo removes created keys again, but only while they are empty.
        /// </summary>
        [JsonPropertyName("created_key_root")]
        public string? CreatedKeyRoot { get; set; }

        [JsonPropertyName("before")]
        public RegistryValueSnapshot Before { get; set; } = RegistryValueSnapshot.Missing();

        [JsonPropertyName("after")]
        public RegistryValueSnapshot After { get; set; } = RegistryValueSnapshot.Missing();

        /// <summary>Verified .reg export of the key taken before the change (empty if the key did not exist).</summary>
        [JsonPropertyName("backup_file")]
        public string? BackupFile { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        public override string ToString() =>
            Operation == RegistryOperation.DeleteKeyTree
                ? $"Delete key {KeyPath}"
                : $"{KeyPath}\\{ValueName}: {Before} -> {After}";
    }

    /// <summary>
    /// Converts registry values to and from strings without losing type information.
    /// </summary>
    public static class RegistryValueCodec
    {
        public static string Encode(object value, RegistryValueKind kind)
        {
            return kind switch
            {
                RegistryValueKind.String or RegistryValueKind.ExpandString => Convert.ToString(value) ?? string.Empty,
                RegistryValueKind.DWord => unchecked((int)Convert.ToInt64(value)).ToString(System.Globalization.CultureInfo.InvariantCulture),
                RegistryValueKind.QWord => Convert.ToInt64(value).ToString(System.Globalization.CultureInfo.InvariantCulture),
                RegistryValueKind.MultiString => JsonSerializer.Serialize(value as string[] ?? Array.Empty<string>()),
                _ => Convert.ToBase64String(value as byte[] ?? Array.Empty<byte>())
            };
        }

        public static object Decode(string data, RegistryValueKind kind)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return kind switch
            {
                RegistryValueKind.String or RegistryValueKind.ExpandString => data,
                RegistryValueKind.DWord => int.Parse(data, inv),
                RegistryValueKind.QWord => long.Parse(data, inv),
                RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(data) ?? Array.Empty<string>(),
                _ => Convert.FromBase64String(data)
            };
        }

        /// <summary>
        /// Registry kinds that cannot be written back through RegistryKey.SetValue
        /// (Unknown/None) are stored and restored as Binary.
        /// </summary>
        public static RegistryValueKind WritableKind(RegistryValueKind kind) =>
            kind is RegistryValueKind.Unknown or RegistryValueKind.None ? RegistryValueKind.Binary : kind;
    }
}
