using System.Management;
using System.Text.Json;
using KWin.Models;
using KWin.Utils;

namespace KWin.Core
{
    /// <summary>
    /// Restore points, undo history and change capture.
    ///
    /// While an operation is running (BeginOperation .. EndOperation), every verified
    /// registry change made through <see cref="RegistryHelper"/> is captured. Captured
    /// changes are attached to the next PushUndoAction call, or to an automatic undo
    /// entry at EndOperation, so no registry change is ever left without an undo record.
    /// The undo history is saved to undo.json and survives restarts.
    /// </summary>
    /// <summary>What happened to the registry during one operation.</summary>
    public sealed record OperationCapture(
        List<RegistryChange> Changes,
        string? UndoId,
        bool HasSessionStep,
        List<string> Failures);

    public class SafetyEngine
    {
        public const int MaxUndoDepth = 20;

        private readonly RegistryHelper _registry;
        private readonly string _undoFile;
        private readonly List<UndoAction> _undo = new(); // oldest first
        private readonly object _lock = new();

        private readonly List<RegistryChange> _pending = new();
        private readonly List<RegistryChange> _operationChanges = new();
        private readonly List<string> _operationFailures = new();
        private bool _capturing;
        private bool _undoPushedThisOperation;
        private string? _lastPushedId;

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public SafetyEngine(RegistryHelper registry, string? dataDirectory = null)
        {
            _registry = registry;
            string dir = dataDirectory ?? AppPaths.DataDirectory;
            Directory.CreateDirectory(dir);
            _undoFile = Path.Combine(dir, "undo.json");
            _registry.ChangeRecorded += OnChangeRecorded;
            _registry.ChangeFailed += OnChangeFailed;
            LoadUndo();
        }

        public string BackupDirectory => _registry.BackupDirectory;

        public int UndoCount { get { lock (_lock) return _undo.Count; } }

        public string? LastUndoDescription { get { lock (_lock) return _undo.Count > 0 ? _undo[^1].Description : null; } }

        public UndoAction? PeekUndo() { lock (_lock) return _undo.Count > 0 ? _undo[^1] : null; }

        /// <summary>Newest first.</summary>
        public IReadOnlyList<UndoAction> UndoActions { get { lock (_lock) return _undo.AsEnumerable().Reverse().ToList(); } }

        #region Change capture

        private void OnChangeRecorded(RegistryChange change)
        {
            lock (_lock)
            {
                if (!_capturing) return;
                _pending.Add(change);
                _operationChanges.Add(change);
            }
        }

        private void OnChangeFailed(string target, string reason)
        {
            lock (_lock)
            {
                if (_capturing) _operationFailures.Add($"{target}: {reason}");
            }
        }

        /// <summary>Starts capturing registry changes for an operation.</summary>
        public void BeginOperation()
        {
            lock (_lock)
            {
                _pending.Clear();
                _operationChanges.Clear();
                _operationFailures.Clear();
                _capturing = true;
                _undoPushedThisOperation = false;
                _lastPushedId = null;
            }
        }

        /// <summary>
        /// Stops capturing. Changes not yet attached to an undo entry get an automatic one.
        /// Returns every change made during the operation and the id of the undo entry (if any).
        /// </summary>
        public OperationCapture EndOperation(string operationName)
        {
            List<RegistryChange> leftovers;
            lock (_lock)
            {
                _capturing = false;
                leftovers = new List<RegistryChange>(_pending);
                _pending.Clear();
            }

            if (leftovers.Count > 0)
            {
                PushUndoAction(new UndoAction { Description = operationName, RegistryChanges = leftovers });
            }

            lock (_lock)
            {
                bool sessionStep = _undoPushedThisOperation &&
                    _undo.Any(u => u.Id == _lastPushedId && u.HadSessionStep);
                return new OperationCapture(
                    new List<RegistryChange>(_operationChanges),
                    _undoPushedThisOperation ? _lastPushedId : null,
                    sessionStep,
                    new List<string>(_operationFailures));
            }
        }

        #endregion

        #region Undo

        /// <summary>
        /// Adds an undo entry. Registry changes captured since the last push are attached to it.
        /// </summary>
        public void PushUndoAction(UndoAction action)
        {
            try
            {
                lock (_lock)
                {
                    if (_pending.Count > 0)
                    {
                        action.RegistryChanges.AddRange(_pending);
                        _pending.Clear();
                    }
                    action.HadSessionStep = action.UndoDelegate != null;

                    // Nothing to undo at all: don't create an empty entry.
                    if (action.RegistryChanges.Count == 0 && action.UndoDelegate == null &&
                        action.BackupFiles.All(string.IsNullOrEmpty))
                        return;

                    action.BackupFiles.RemoveAll(string.IsNullOrEmpty);
                    _undo.Add(action);
                    while (_undo.Count > MaxUndoDepth) _undo.RemoveAt(0);

                    _undoPushedThisOperation = true;
                    _lastPushedId = action.Id;
                    SaveUndo();
                }

                Logger.Instance.Info("PushUndoAction", action.Description, "registered",
                    newValue: $"changes={action.RegistryChanges.Count}, depth={UndoCount}");
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("PushUndoAction", action.Description, ex);
            }
        }

        /// <summary>
        /// Undoes the most recent action: reverts its registry changes (newest first), then runs
        /// its session-only step if still available. Reports partial undo honestly.
        /// </summary>
        public async Task<Result<bool>> UndoLastAction()
        {
            UndoAction? action;
            lock (_lock)
            {
                if (_undo.Count == 0)
                    return Result<bool>.Fail("No actions to undo.");
                action = _undo[^1];
                _undo.RemoveAt(_undo.Count - 1);
                SaveUndo();
            }
            return await RunUndoAsync(action);
        }

        /// <summary>True if this undo entry still exists (not undone yet and not trimmed).</summary>
        public bool HasUndo(string? id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            lock (_lock) return _undo.Any(u => u.Id == id);
        }

        /// <summary>
        /// Whether one specific (possibly older) action can be undone safely. It cannot when a later
        /// action changed one of the same registry values or keys: undoing out of order would
        /// overwrite that later change. The reason names the action to undo first.
        /// </summary>
        public bool CanUndo(string? id, out string reason)
        {
            reason = string.Empty;
            lock (_lock)
            {
                int index = _undo.FindIndex(u => u.Id == id);
                if (index < 0)
                {
                    reason = "not_available";
                    return false;
                }
                var mine = _undo[index].RegistryChanges.Select(ChangeId).ToList();
                for (int i = _undo.Count - 1; i > index; i--)
                {
                    var later = _undo[i];
                    if (later.RegistryChanges.Select(ChangeId).Any(l => mine.Any(m => Overlaps(m, l))))
                    {
                        reason = later.Description;
                        return false;
                    }
                }
                return true;
            }
        }

        private static string ChangeId(RegistryChange c) =>
            (c.Operation == RegistryOperation.DeleteKeyTree ? c.KeyPath + "\\" : $"{c.KeyPath}\\{c.ValueName}").ToUpperInvariant();

        /// <summary>Same value, or one is inside a key the other deleted/created.</summary>
        private static bool Overlaps(string a, string b) =>
            a == b || (a.EndsWith("\\") && b.StartsWith(a)) || (b.EndsWith("\\") && a.StartsWith(b));

        /// <summary>Undoes one specific action if <see cref="CanUndo"/> allows it.</summary>
        public async Task<Result<bool>> UndoByIdAsync(string id)
        {
            UndoAction action;
            lock (_lock)
            {
                if (!CanUndo(id, out var reason))
                    return Result<bool>.Fail(reason == "not_available"
                        ? "This change can no longer be undone (already undone, or older than the last 20 changes)."
                        : $"Undo \"{reason}\" first: it changed the same settings later.");
                int index = _undo.FindIndex(u => u.Id == id);
                action = _undo[index];
                _undo.RemoveAt(index);
                SaveUndo();
            }
            return await RunUndoAsync(action);
        }

        private async Task<Result<bool>> RunUndoAsync(UndoAction action)
        {
            Logger.Instance.Info("UndoLastAction", action.Description, "starting");
            var errors = new List<string>();

            try
            {
                if (action.RegistryChanges.Count > 0)
                {
                    for (int i = action.RegistryChanges.Count - 1; i >= 0; i--)
                    {
                        var r = await _registry.RevertChangeAsync(action.RegistryChanges[i]);
                        if (!r.Success) errors.Add(r.ErrorMessage ?? "Unknown error");
                    }
                }
                else if (action.BackupFiles.Count > 0)
                {
                    foreach (var file in action.BackupFiles)
                    {
                        var r = await _registry.RestoreFromBackup(file);
                        if (!r.Success) errors.Add(r.ErrorMessage ?? "Unknown error");
                    }
                }

                if (action.UndoDelegate != null)
                {
                    var r = await action.UndoDelegate();
                    if (!r.Success) errors.Add(r.ErrorMessage ?? "Unknown error");
                }
                else if (action.HadSessionStep)
                {
                    errors.Add("Part of this action (a non-registry step such as a power plan or service state) " +
                               "could only be undone in the session where it was made. Registry changes were reverted.");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{ex.GetType().Name}: {ex.Message}");
            }

            if (errors.Count > 0)
            {
                Logger.Instance.Error("UndoLastAction", action.Description, string.Join(" | ", errors));
                return Result<bool>.Fail(string.Join("\n", errors));
            }

            Logger.Instance.Info("UndoLastAction", action.Description, "success");
            return Result<bool>.Ok(true);
        }

        /// <summary>Undoes all actions, newest first.</summary>
        public async Task<Result<bool>> UndoAllActions()
        {
            var errors = new List<string>();
            while (UndoCount > 0)
            {
                var result = await UndoLastAction();
                if (!result.Success) errors.Add(result.ErrorMessage ?? "Unknown error");
            }
            return errors.Count > 0
                ? Result<bool>.Fail($"Some undo operations failed:\n{string.Join("\n", errors)}")
                : Result<bool>.Ok(true);
        }

        private void LoadUndo()
        {
            try
            {
                if (!File.Exists(_undoFile)) return;
                var loaded = JsonSerializer.Deserialize<List<UndoAction>>(File.ReadAllText(_undoFile), JsonOptions);
                if (loaded != null) _undo.AddRange(loaded.TakeLast(MaxUndoDepth));
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("SafetyEngine.LoadUndo", _undoFile, ex);
                AppPaths.QuarantineCorruptFile(_undoFile);
            }
        }

        private void SaveUndo()
        {
            try
            {
                AppPaths.WriteAllTextAtomic(_undoFile, JsonSerializer.Serialize(_undo, JsonOptions));
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("SafetyEngine.SaveUndo", _undoFile, ex);
            }
        }

        #endregion

        #region Restore points

        /// <summary>
        /// Requests a System Restore point and verifies whether Windows actually created one.
        /// Windows silently skips creation if a restore point was made in the last 24 hours
        /// (SystemRestorePointCreationFrequency); that case is reported as SkippedRecentExists.
        /// This is a blocking WMI call: run it on a background thread.
        /// </summary>
        public RestorePointOutcome CreateRestorePoint(string operationName)
        {
            string description = $"K-win: {operationName}";
            if (description.Length > 64) description = description[..64]; // Windows truncates long descriptions

            if (DevMode.IsUiPreview)
                return new RestorePointOutcome { Status = RestorePointStatus.Failed, Message = DevMode.BlockedMessage };

            if (!ElevationHelper.IsAdministrator)
            {
                return new RestorePointOutcome
                {
                    Status = RestorePointStatus.SkippedNotAdmin,
                    Message = "Restore points require administrator rights."
                };
            }

            try
            {
                Logger.Instance.Info("CreateRestorePoint", description, "starting");
                var latestBefore = GetLatestRestorePoint();

                var scope = new ManagementScope(@"\\.\root\default");
                using var sr = new ManagementClass(scope, new ManagementPath("SystemRestore"), new ObjectGetOptions());
                using var inParams = sr.GetMethodParameters("CreateRestorePoint");
                inParams["Description"] = description;
                inParams["RestorePointType"] = 12; // MODIFY_SETTINGS
                inParams["EventType"] = 100;       // BEGIN_SYSTEM_CHANGE

                using var outParams = sr.InvokeMethod("CreateRestorePoint", inParams, null);
                int returnValue = Convert.ToInt32(outParams["ReturnValue"]);

                if (returnValue != 0)
                {
                    string reason = returnValue switch
                    {
                        1058 => "System Restore is turned off or its service is disabled.",
                        1068 => "A service System Restore depends on could not start.",
                        unchecked((int)0x80070422) => "System Restore is turned off or its service is disabled.",
                        _ => $"Windows returned error code {returnValue}."
                    };
                    Logger.Instance.Error("CreateRestorePoint", description, reason);
                    return new RestorePointOutcome { Status = RestorePointStatus.Failed, Message = reason };
                }

                var latestAfter = GetLatestRestorePoint();
                if (latestAfter != null && (latestBefore == null || latestAfter.Value.Sequence > latestBefore.Value.Sequence))
                {
                    Logger.Instance.Info("CreateRestorePoint", description, "verified",
                        newValue: $"seq={latestAfter.Value.Sequence}");
                    return new RestorePointOutcome
                    {
                        Status = RestorePointStatus.Created,
                        Message = "Restore point created and verified.",
                        SequenceNumber = latestAfter.Value.Sequence,
                        LatestRestorePointTime = latestAfter.Value.Created
                    };
                }

                if (latestBefore != null)
                {
                    string msg = $"Windows did not create a new restore point because one was created recently " +
                                 $"({latestBefore.Value.Created:g}). That earlier restore point is still available.";
                    Logger.Instance.Warn("CreateRestorePoint", description, msg);
                    return new RestorePointOutcome
                    {
                        Status = RestorePointStatus.SkippedRecentExists,
                        Message = msg,
                        SequenceNumber = latestBefore.Value.Sequence,
                        LatestRestorePointTime = latestBefore.Value.Created
                    };
                }

                const string noneMsg = "Windows reported success, but no restore point could be found. System Restore may be turned off for the system drive.";
                Logger.Instance.Error("CreateRestorePoint", description, noneMsg);
                return new RestorePointOutcome { Status = RestorePointStatus.Failed, Message = noneMsg };
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("CreateRestorePoint", description, ex);
                return new RestorePointOutcome
                {
                    Status = RestorePointStatus.Failed,
                    Message = $"{ex.GetType().Name}: {ex.Message}"
                };
            }
        }

        private static (uint Sequence, DateTime Created)? GetLatestRestorePoint()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\default",
                    "SELECT SequenceNumber, CreationTime FROM SystemRestore");
                (uint, DateTime)? latest = null;
                foreach (ManagementObject mo in searcher.Get())
                {
                    using (mo)
                    {
                        uint seq = Convert.ToUInt32(mo["SequenceNumber"]);
                        DateTime created = ManagementDateTimeConverter.ToDateTime(mo["CreationTime"]?.ToString() ?? "");
                        if (latest == null || seq > latest.Value.Item1)
                            latest = (seq, created);
                    }
                }
                return latest;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        /// <summary>Deletes .reg backups older than the given number of days, keeping any still needed for undo.</summary>
        public int CleanOldBackups(int daysToKeep = 30)
        {
            int cleaned = 0;
            try
            {
                HashSet<string> inUse;
                lock (_lock)
                {
                    inUse = _undo.SelectMany(u => u.RegistryChanges.Select(c => c.BackupFile ?? "").Concat(u.BackupFiles))
                        .Where(f => !string.IsNullOrEmpty(f))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                }

                var cutoff = DateTime.Now.AddDays(-daysToKeep);
                foreach (var file in Directory.GetFiles(BackupDirectory, "*.reg"))
                {
                    if (inUse.Contains(file)) continue;
                    if (File.GetCreationTime(file) < cutoff)
                    {
                        File.Delete(file);
                        cleaned++;
                    }
                }
                Logger.Instance.Info("CleanOldBackups", BackupDirectory, "success", newValue: $"Deleted {cleaned} files");
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("CleanOldBackups", BackupDirectory, ex);
            }
            return cleaned;
        }
    }
}
