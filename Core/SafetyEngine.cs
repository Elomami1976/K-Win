using System.Management;
using KWin.Models;
using KWin.Utils;

namespace KWin.Core
{
    /// <summary>
    /// Central safety system responsible for creating restore points, managing backups,
    /// and maintaining an undo stack for all system modifications.
    /// This is the guardian of system integrity — every modification flows through here.
    /// </summary>
    public class SafetyEngine
    {
        private readonly Stack<UndoAction> _undoStack = new();
        private readonly string _backupDirectory;

        /// <summary>Maximum number of undo operations to keep in the stack.</summary>
        public const int MaxUndoDepth = 5;

        /// <summary>Gets the number of available undo operations.</summary>
        public int UndoCount => _undoStack.Count;

        /// <summary>Gets the description of the most recent undoable action.</summary>
        public string? LastUndoDescription => _undoStack.Count > 0 ? _undoStack.Peek().Description : null;

        /// <summary>Gets all available undo actions (read-only).</summary>
        public IReadOnlyList<UndoAction> UndoActions => _undoStack.ToList().AsReadOnly();

        public SafetyEngine()
        {
            _backupDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "K-win", "backups");
            Directory.CreateDirectory(_backupDirectory);
        }

        /// <summary>Gets the backup directory path.</summary>
        public string BackupDirectory => _backupDirectory;

        /// <summary>
        /// Creates a Windows System Restore Point using WMI before any system modification.
        /// Description format: "K-win Win11: [Operation] on [Date]"
        /// </summary>
        /// <param name="operationName">Name of the operation being performed.</param>
        /// <returns>Result indicating success or failure.</returns>
        public Result<bool> CreateRestorePoint(string operationName)
        {
            string description = $"K-win Win11: {operationName} on {DateTime.Now:yyyy-MM-dd HH:mm}";

            try
            {
                Logger.Instance.Info("CreateRestorePoint", description, "starting");

                // Use WMI to create a system restore point
                var scope = new ManagementScope(@"\\.\root\default");
                var path = new ManagementPath("SystemRestore");
                var options = new ObjectGetOptions();

                using var process = new ManagementClass(scope, path, options);
                var inParams = process.GetMethodParameters("CreateRestorePoint");
                inParams["Description"] = description;
                inParams["RestorePointType"] = 12; // APPLICATION_INSTALL
                inParams["EventType"] = 100;       // BEGIN_SYSTEM_CHANGE

                var outParams = process.InvokeMethod("CreateRestorePoint", inParams, null);
                var returnValue = Convert.ToInt32(outParams["ReturnValue"]);

                if (returnValue == 0)
                {
                    Logger.Instance.Info("CreateRestorePoint", description, "success");
                    return Result<bool>.Ok(true);
                }
                else
                {
                    string error = $"WMI returned code {returnValue}";
                    Logger.Instance.Error("CreateRestorePoint", description, error);
                    return Result<bool>.Fail(error);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("CreateRestorePoint", description, ex);
                return Result<bool>.Fail(ex);
            }
        }

        /// <summary>
        /// Pushes an undo action onto the stack. Trims oldest items if stack exceeds MaxUndoDepth.
        /// </summary>
        /// <param name="action">The undo action to register.</param>
        public void PushUndoAction(UndoAction action)
        {
            try
            {
                // Trim if at capacity
                if (_undoStack.Count >= MaxUndoDepth)
                {
                    var items = _undoStack.ToList();
                    _undoStack.Clear();
                    // Keep most recent (MaxUndoDepth - 1) to make room
                    for (int i = Math.Min(items.Count - 1, MaxUndoDepth - 2); i >= 0; i--)
                    {
                        _undoStack.Push(items[i]);
                    }
                }

                _undoStack.Push(action);
                Logger.Instance.Info("PushUndoAction", action.Description, "registered",
                    newValue: $"Stack depth: {_undoStack.Count}");
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("PushUndoAction", action.Description, ex);
            }
        }

        /// <summary>
        /// Undoes the most recent action on the stack.
        /// </summary>
        /// <returns>Result indicating success or failure of the undo operation.</returns>
        public async Task<Result<bool>> UndoLastAction()
        {
            try
            {
                if (_undoStack.Count == 0)
                    return Result<bool>.Fail("No actions to undo.");

                var action = _undoStack.Pop();
                Logger.Instance.Info("UndoLastAction", action.Description, "starting");

                if (action.UndoDelegate != null)
                {
                    var result = await action.UndoDelegate();
                    if (result.Success)
                    {
                        Logger.Instance.Info("UndoLastAction", action.Description, "success");
                    }
                    else
                    {
                        Logger.Instance.Error("UndoLastAction", action.Description,
                            result.ErrorMessage ?? "Unknown error");
                    }
                    return result;
                }

                // Fallback: restore from backup files
                if (action.BackupFiles.Count > 0)
                {
                    return await RestoreFromBackupFiles(action.BackupFiles);
                }

                return Result<bool>.Fail("No undo delegate or backup files available.");
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("UndoLastAction", "unknown", ex);
                return Result<bool>.Fail(ex);
            }
        }

        /// <summary>
        /// Undoes all actions in the stack, from newest to oldest.
        /// </summary>
        /// <returns>Result indicating overall success or failure.</returns>
        public async Task<Result<bool>> UndoAllActions()
        {
            var errors = new List<string>();

            while (_undoStack.Count > 0)
            {
                var result = await UndoLastAction();
                if (!result.Success)
                    errors.Add(result.ErrorMessage ?? "Unknown error");
            }

            if (errors.Count > 0)
                return Result<bool>.Fail($"Some undo operations failed:\n{string.Join("\n", errors)}");

            return Result<bool>.Ok(true);
        }

        /// <summary>
        /// Restores registry keys from a list of .reg backup files.
        /// </summary>
        private async Task<Result<bool>> RestoreFromBackupFiles(List<string> backupFiles)
        {
            var errors = new List<string>();

            foreach (var backupFile in backupFiles)
            {
                try
                {
                    if (!File.Exists(backupFile))
                    {
                        errors.Add($"Backup file not found: {backupFile}");
                        continue;
                    }

                    // Import .reg file using reg.exe
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "reg.exe",
                        Arguments = $"import \"{backupFile}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var proc = System.Diagnostics.Process.Start(psi);
                    if (proc != null)
                    {
                        await proc.WaitForExitAsync();
                        if (proc.ExitCode != 0)
                        {
                            string err = await proc.StandardError.ReadToEndAsync();
                            errors.Add($"Failed to restore {backupFile}: {err}");
                        }
                        else
                        {
                            Logger.Instance.Info("RestoreBackup", backupFile, "success");
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Exception restoring {backupFile}: {ex.Message}");
                }
            }

            if (errors.Count > 0)
                return Result<bool>.Fail(string.Join("\n", errors));

            return Result<bool>.Ok(true);
        }

        /// <summary>
        /// Cleans up backup files older than the specified number of days.
        /// </summary>
        /// <param name="daysToKeep">Number of days of backups to retain.</param>
        /// <returns>Number of files deleted.</returns>
        public int CleanOldBackups(int daysToKeep = 30)
        {
            int cleaned = 0;
            try
            {
                var cutoff = DateTime.Now.AddDays(-daysToKeep);
                foreach (var file in Directory.GetFiles(_backupDirectory, "*.reg"))
                {
                    if (File.GetCreationTime(file) < cutoff)
                    {
                        File.Delete(file);
                        cleaned++;
                    }
                }
                Logger.Instance.Info("CleanOldBackups", _backupDirectory, "success",
                    newValue: $"Deleted {cleaned} files");
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("CleanOldBackups", _backupDirectory, ex);
            }
            return cleaned;
        }
    }
}
