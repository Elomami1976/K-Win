using System.Text.Json;
using System.Text.Json.Serialization;
using KWin.Utils;

namespace KWin.Services
{
    /// <summary>User preferences stored locally in %AppData%\K-win\settings.json.</summary>
    public class AppSettings
    {
        /// <summary>"dark" or "light".</summary>
        [JsonPropertyName("theme")]
        public string Theme { get; set; } = "dark";

        [JsonPropertyName("auto_restore_point")]
        public bool AutoRestorePoint { get; set; } = true;

        [JsonPropertyName("backup_retention_days")]
        public int BackupRetentionDays { get; set; } = 30;

        /// <summary>Dashboard monitoring refresh interval in seconds (1, 2, 3, 5 or 10).</summary>
        [JsonPropertyName("monitor_interval_seconds")]
        public int MonitorIntervalSeconds { get; set; } = 2;

        /// <summary>Optional GitHub update check at startup. Off by default.</summary>
        [JsonPropertyName("check_updates_on_start")]
        public bool CheckUpdatesOnStart { get; set; }

        [JsonPropertyName("last_update_check_utc")]
        public DateTime? LastUpdateCheckUtc { get; set; }

        [JsonIgnore]
        public bool IsDark => !string.Equals(Theme, "light", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Loads and saves <see cref="AppSettings"/>. The language is stored by Loc in language.json.</summary>
    public class SettingsService
    {
        private readonly string _path;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public AppSettings Current { get; private set; }

        public SettingsService(string? dataDirectory = null)
        {
            string dir = dataDirectory ?? AppPaths.DataDirectory;
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "settings.json");
            Current = Load();
        }

        private AppSettings Load()
        {
            try
            {
                if (!File.Exists(_path)) return new AppSettings();
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new AppSettings();
                s.BackupRetentionDays = Math.Clamp(s.BackupRetentionDays, 1, 365);
                s.MonitorIntervalSeconds = SystemMonitorService.NormalizeInterval(s.MonitorIntervalSeconds);
                return s;
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("Settings.Load", _path, ex);
                AppPaths.QuarantineCorruptFile(_path);
                return new AppSettings();
            }
        }

        public void Save()
        {
            try
            {
                AppPaths.WriteAllTextAtomic(_path, JsonSerializer.Serialize(Current, JsonOptions));
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("Settings.Save", _path, ex);
            }
        }
    }
}
