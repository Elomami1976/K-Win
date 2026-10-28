using System.Text.Json.Serialization;

namespace KWin.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OperationStatus
    {
        Completed,
        Failed,
        Cancelled,
        Skipped,
        RestartRequired
    }

    /// <summary>How much of an operation can be undone.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum Reversibility
    {
        /// <summary>Every change was recorded and can be undone, also after a restart.</summary>
        Full,
        /// <summary>Some parts can be undone (or only during this session).</summary>
        Partial,
        /// <summary>Cannot be undone (e.g. deleted files).</summary>
        None
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RestorePointStatus
    {
        NotRequested,
        Created,
        /// <summary>Windows skipped it because another restore point was created recently.</summary>
        SkippedRecentExists,
        /// <summary>Not attempted because K-Win was not running as administrator.</summary>
        SkippedNotAdmin,
        Failed
    }

    /// <summary>Outcome of a restore point request, verified against the actual restore point list.</summary>
    public class RestorePointOutcome
    {
        public RestorePointStatus Status { get; set; }
        public string Message { get; set; } = string.Empty;
        public uint? SequenceNumber { get; set; }
        public DateTime? LatestRestorePointTime { get; set; }

        public bool IsProtected => Status is RestorePointStatus.Created or RestorePointStatus.SkippedRecentExists;
    }

    /// <summary>One entry of the operation history (saved to history.json).</summary>
    public class OperationRecord
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("started")]
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("completed")]
        public DateTime? CompletedUtc { get; set; }

        [JsonPropertyName("status")]
        public OperationStatus Status { get; set; }

        [JsonPropertyName("reversibility")]
        public Reversibility Reversibility { get; set; } = Reversibility.None;

        [JsonPropertyName("restore_point")]
        public RestorePointStatus RestorePoint { get; set; } = RestorePointStatus.NotRequested;

        [JsonPropertyName("registry_changes")]
        public List<RegistryChange> RegistryChanges { get; set; } = new();

        /// <summary>Id of the matching undo entry, if one was created.</summary>
        [JsonPropertyName("undo_id")]
        public string? UndoId { get; set; }

        [JsonPropertyName("details")]
        public string? Details { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
