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
    /// Stored in the undo stack for rollback capability.
    /// </summary>
    public class UndoAction
    {
        /// <summary>Human-readable description of the action.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>When this action was performed.</summary>
        public DateTime PerformedAt { get; set; } = DateTime.UtcNow;

        /// <summary>The delegate that reverses this action.</summary>
        public Func<Task<Result<bool>>>? UndoDelegate { get; set; }

        /// <summary>List of registry backup file paths associated with this action.</summary>
        public List<string> BackupFiles { get; set; } = new();

        /// <summary>Related change logs for this action.</summary>
        public List<ChangeLog> Changes { get; set; } = new();
    }
}
