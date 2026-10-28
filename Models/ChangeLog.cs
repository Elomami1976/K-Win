using System.Text.Json.Serialization;

namespace KWin.Models
{
    /// <summary>
    /// Records a single change operation for audit trail and undo functionality.
    /// Serialized as JSON Lines to the log file.
    /// </summary>
    public class ChangeLog
    {
        /// <summary>UTC timestamp of the operation.</summary>
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>Log level (INFO, WARN, ERROR).</summary>
        [JsonPropertyName("level")]
        public string Level { get; set; } = "INFO";

        /// <summary>Name of the operation performed.</summary>
        [JsonPropertyName("operation")]
        public string Operation { get; set; } = string.Empty;

        /// <summary>Target of the operation (registry key, file path, service name).</summary>
        [JsonPropertyName("target")]
        public string Target { get; set; } = string.Empty;

        /// <summary>Result status (success, failed, skipped).</summary>
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        /// <summary>Value before the change (for undo).</summary>
        [JsonPropertyName("old_value")]
        public string? OldValue { get; set; }

        /// <summary>Value after the change.</summary>
        [JsonPropertyName("new_value")]
        public string? NewValue { get; set; }

        /// <summary>Whether a backup was created before the operation.</summary>
        [JsonPropertyName("backup_created")]
        public string? BackupCreated { get; set; }

        /// <summary>Path to the backup file if one was created.</summary>
        [JsonPropertyName("backup_path")]
        public string? BackupPath { get; set; }

        /// <summary>Error details if the operation failed.</summary>
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        /// <summary>Unique operation identifier for correlation.</summary>
        [JsonPropertyName("operation_id")]
        public string OperationId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    }

    /// <summary>
    /// Represents an action that can be undone.
    /// Registry changes are saved to disk so they can be undone after a restart.
    /// Non-registry steps (UndoDelegate, e.g. switching the power plan back) only exist
    /// for the current session.
    /// </summary>
    public class UndoAction
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

        /// <summary>Human-readable description of the action.</summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>When this action was performed (UTC).</summary>
        [JsonPropertyName("performed_at")]
        public DateTime PerformedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Extra, non-registry undo step for this session only (not saved to disk).
        /// It runs after the registry changes have been reverted.
        /// </summary>
        [JsonIgnore]
        public Func<Task<Result<bool>>>? UndoDelegate { get; set; }

        /// <summary>True if this action had a session-only undo step when it was created.</summary>
        [JsonPropertyName("had_session_step")]
        public bool HadSessionStep { get; set; }

        /// <summary>Precisely reversible registry changes, newest last.</summary>
        [JsonPropertyName("registry_changes")]
        public List<RegistryChange> RegistryChanges { get; set; } = new();

        /// <summary>Legacy: .reg backup files (used only when no RegistryChanges were recorded).</summary>
        [JsonPropertyName("backup_files")]
        public List<string> BackupFiles { get; set; } = new();

        /// <summary>
        /// True when part of the undo is no longer available
        /// (the session-only step was lost because the app restarted).
        /// </summary>
        [JsonIgnore]
        public bool IsPartial => HadSessionStep && UndoDelegate == null;
    }
}
