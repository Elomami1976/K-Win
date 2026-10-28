using System.Diagnostics;
using Microsoft.Win32;
using KWin.Models;
using KWin.Utils;

namespace KWin.Core
{
    /// <summary>
    /// Safe registry operations.
    /// Every write: (1) records the exact original state, (2) exports a verified .reg backup
    /// when the key already exists, (3) writes, (4) reads back to verify, and
    /// (5) publishes a <see cref="RegistryChange"/> that can be precisely reverted later.
    /// If the backup cannot be verified, the write is not performed.
    /// </summary>
    public class RegistryHelper
    {
        private readonly string _backupDirectory;

        /// <summary>Raised after every verified change. SafetyEngine uses it to build undo records.</summary>
        public event Action<RegistryChange>? ChangeRecorded;

        /// <summary>Raised when a write is refused or fails (target, reason), so callers can't miss it.</summary>
        public event Action<string, string>? ChangeFailed;

        public RegistryHelper(string? backupDirectory = null)
        {
            _backupDirectory = backupDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "K-win", "backups");
            Directory.CreateDirectory(_backupDirectory);
        }

        public string BackupDirectory => _backupDirectory;

        private static string RegExePath => Path.Combine(Environment.SystemDirectory, "reg.exe");

        #region Reads

        /// <summary>Reads a registry value. Returns Ok(null) if the key or value doesn't exist.</summary>
        public Result<object?> GetValue(string keyPath, string valueName)
        {
            try
            {
                if (!Validator.IsValidRegistryPath(keyPath, out string error))
                    return Result<object?>.Fail(error);

                var (root, subKey) = ParseKeyPath(keyPath);
                if (root == null)
                    return Result<object?>.Fail($"Invalid registry root in path: {keyPath}");

                using var key = root.OpenSubKey(subKey, false);
                return Result<object?>.Ok(key?.GetValue(valueName));
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("GetValue", $"{keyPath}\\{valueName}", ex);
                return Result<object?>.Fail(ex);
            }
        }

        /// <summary>Checks if a registry key exists.</summary>
        public Result<bool> KeyExists(string keyPath)
        {
            try
            {
                if (!Validator.IsValidRegistryPath(keyPath, out string error))
                    return Result<bool>.Fail(error);

                var (root, subKey) = ParseKeyPath(keyPath);
                if (root == null)
                    return Result<bool>.Fail($"Invalid registry root in path: {keyPath}");

                using var key = root.OpenSubKey(subKey, false);
                return Result<bool>.Ok(key != null);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("KeyExists", keyPath, ex);
                return Result<bool>.Fail(ex);
            }
        }

        /// <summary>Gets all value names under a registry key.</summary>
        public Result<string[]> GetValueNames(string keyPath)
        {
            try
            {
                if (!Validator.IsValidRegistryPath(keyPath, out string error))
                    return Result<string[]>.Fail(error);

                var (root, subKey) = ParseKeyPath(keyPath);
                if (root == null)
                    return Result<string[]>.Fail($"Invalid registry root in path: {keyPath}");

                using var key = root.OpenSubKey(subKey, false);
                return Result<string[]>.Ok(key?.GetValueNames() ?? Array.Empty<string>());
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("GetValueNames", keyPath, ex);
                return Result<string[]>.Fail(ex);
            }
        }

        /// <summary>Gets all subkey names under a registry key.</summary>
        public Result<string[]> GetSubKeyNames(string keyPath)
        {
            try
            {
                if (!Validator.IsValidRegistryPath(keyPath, out string error))
                    return Result<string[]>.Fail(error);

                var (root, subKey) = ParseKeyPath(keyPath);
                if (root == null)
                    return Result<string[]>.Fail($"Invalid registry root in path: {keyPath}");

                using var key = root.OpenSubKey(subKey, false);
                return Result<string[]>.Ok(key?.GetSubKeyNames() ?? Array.Empty<string>());
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("GetSubKeyNames", keyPath, ex);
                return Result<string[]>.Fail(ex);
            }
        }

        /// <summary>
        /// Reads the exact state of a value (existence, kind and unexpanded data).
        /// </summary>
        public Result<(bool KeyExists, RegistryValueSnapshot Value)> ReadSnapshot(string keyPath, string valueName)
        {
            try
            {
                if (!Validator.IsValidRegistryPath(keyPath, out string error))
                    return Result<(bool, RegistryValueSnapshot)>.Fail(error);

                var (root, subKey) = ParseKeyPath(keyPath);
                if (root == null)
                    return Result<(bool, RegistryValueSnapshot)>.Fail($"Invalid registry root in path: {keyPath}");

                using var key = root.OpenSubKey(subKey, false);
                if (key == null)
                    return Result<(bool, RegistryValueSnapshot)>.Ok((false, RegistryValueSnapshot.Missing()));

                return Result<(bool, RegistryValueSnapshot)>.Ok((true, Snapshot(key, valueName)));
            }
            catch (Exception ex)
            {
                return Result<(bool, RegistryValueSnapshot)>.Fail(ex);
            }
        }

        private static RegistryValueSnapshot Snapshot(RegistryKey key, string valueName)
        {
            object? raw = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (raw == null)
                return RegistryValueSnapshot.Missing();

            var kind = key.GetValueKind(valueName);
            return new RegistryValueSnapshot
            {
                Exists = true,
                Kind = RegistryValueCodec.WritableKind(kind),
                Data = RegistryValueCodec.Encode(raw, RegistryValueCodec.WritableKind(kind))
            };
        }

        #endregion

        #region Writes

        /// <summary>
        /// Sets a registry value. A verified backup is taken first; if it cannot be verified,
        /// nothing is written. Returns the backup file path ("" when the key did not exist yet,
        /// so there was nothing to back up).
        /// </summary>
        public Result<string> SetValueWithBackup(string keyPath, string valueName, object value, RegistryValueKind kind)
            => ReportFailure($"{keyPath}\\{valueName}", SetValueCore(keyPath, valueName, value, kind));

        private Result<string> SetValueCore(string keyPath, string valueName, object value, RegistryValueKind kind)
        {
            string target = $"{keyPath}\\{valueName}";
            try
            {
                DevMode.ThrowIfReadOnly("registry write");
                if (!Validator.IsValidRegistryPath(keyPath, out string error))
                    return Result<string>.Fail(error);

                var (root, subKey) = ParseKeyPath(keyPath);
                if (root == null)
                    return Result<string>.Fail($"Invalid registry root in path: {keyPath}");

                // 1. Capture original state
                var before = ReadSnapshot(keyPath, valueName);
                if (!before.Success)
                    return Result<string>.Fail($"Could not read current value: {before.ErrorMessage}");
                var (keyExisted, beforeValue) = before.Data;

                // 2. Verified backup (only possible when the key exists)
                string backupPath = string.Empty;
                if (keyExisted)
                {
                    var backup = ExportKeyToBackup(keyPath);
                    if (!backup.Success)
                    {
                        Logger.Instance.Error("SetValueWithBackup", target,
                            $"Backup failed, change NOT applied: {backup.ErrorMessage}");
                        return Result<string>.Fail($"Backup failed, so the change was not applied. {backup.ErrorMessage}");
                    }
                    backupPath = backup.Data!;
                }

                string? createdRoot = keyExisted ? null : FindFirstMissingKey(root, keyPath, subKey);

                // 3. Write
                using (var key = root.CreateSubKey(subKey, true))
                {
                    if (key == null)
                        return Result<string>.Fail($"Failed to open/create registry key: {keyPath}");
                    key.SetValue(valueName, value, kind);
                }

                // 4. Verify by reading back
                var expected = new RegistryValueSnapshot
                {
                    Exists = true,
                    Kind = RegistryValueCodec.WritableKind(kind),
                    Data = RegistryValueCodec.Encode(value, RegistryValueCodec.WritableKind(kind))
                };
                var after = ReadSnapshot(keyPath, valueName);
                if (!after.Success || !after.Data.Value.SameAs(expected))
                {
                    Logger.Instance.Error("SetValueWithBackup", target,
                        $"Verification failed: expected {expected}, read {(after.Success ? after.Data.Value.ToString() : after.ErrorMessage)}");
                    return Result<string>.Fail("The value was written but could not be verified.");
                }

                // 5. Record
                var change = new RegistryChange
                {
                    Operation = RegistryOperation.SetValue,
                    KeyPath = keyPath,
                    ValueName = valueName,
                    KeyExisted = keyExisted,
                    CreatedKeyRoot = createdRoot,
                    Before = beforeValue,
                    After = after.Data.Value,
                    BackupFile = backupPath
                };
                Publish(change);

                Logger.Instance.Info("SetValueWithBackup", target, "success",
                    oldValue: beforeValue.ToString(),
                    newValue: after.Data.Value.ToString(),
                    backupCreated: keyExisted ? "true" : "not_needed_key_absent",
                    backupPath: backupPath);

                return Result<string>.Ok(backupPath);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("SetValueWithBackup", target, ex);
                return Result<string>.Fail(ex);
            }
        }

        /// <summary>
        /// Deletes a registry value after a verified backup. Succeeds with "" if there was nothing to delete.
        /// </summary>
        public Result<string> DeleteValueWithBackup(string keyPath, string valueName)
            => ReportFailure($"{keyPath}\\{valueName}", DeleteValueCore(keyPath, valueName));

        private Result<string> DeleteValueCore(string keyPath, string valueName)
        {
            string target = $"{keyPath}\\{valueName}";
            try
            {
                DevMode.ThrowIfReadOnly("registry delete");
                if (!Validator.IsValidRegistryPath(keyPath, out string error))
                    return Result<string>.Fail(error);

                var (root, subKey) = ParseKeyPath(keyPath);
                if (root == null)
                    return Result<string>.Fail($"Invalid registry root in path: {keyPath}");

                var before = ReadSnapshot(keyPath, valueName);
                if (!before.Success)
                    return Result<string>.Fail($"Could not read current value: {before.ErrorMessage}");
                if (!before.Data.KeyExists || !before.Data.Value.Exists)
                    return Result<string>.Ok(string.Empty); // nothing to delete

                var backup = ExportKeyToBackup(keyPath);
                if (!backup.Success)
                    return Result<string>.Fail($"Backup failed, so the value was not deleted. {backup.ErrorMessage}");

                using (var key = root.OpenSubKey(subKey, true))
                {
                    key?.DeleteValue(valueName, false);
                }

                var after = ReadSnapshot(keyPath, valueName);
                if (!after.Success || after.Data.Value.Exists)
                    return Result<string>.Fail("The value could not be verified as deleted.");

                Publish(new RegistryChange
                {
                    Operation = RegistryOperation.DeleteValue,
                    KeyPath = keyPath,
                    ValueName = valueName,
                    KeyExisted = true,
                    Before = before.Data.Value,
                    After = RegistryValueSnapshot.Missing(),
                    BackupFile = backup.Data
                });

                Logger.Instance.Info("DeleteValueWithBackup", target, "success",
                    oldValue: before.Data.Value.ToString(),
                    backupCreated: "true", backupPath: backup.Data);

                return Result<string>.Ok(backup.Data!);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("DeleteValueWithBackup", target, ex);
                return Result<string>.Fail(ex);
            }
        }

        /// <summary>
        /// Deletes a whole key after a verified backup. Undo re-imports the backup.
        /// Succeeds with "" if the key does not exist.
        /// </summary>
        public Result<string> DeleteKeyTreeWithBackup(string keyPath)
            => ReportFailure(keyPath, DeleteKeyTreeCore(keyPath));

        private Result<string> DeleteKeyTreeCore(string keyPath)
        {
            try
            {
                DevMode.ThrowIfReadOnly("registry key delete");
                if (!Validator.IsValidRegistryPath(keyPath, out string error))
                    return Result<string>.Fail(error);

                var (root, subKey) = ParseKeyPath(keyPath);
                if (root == null || string.IsNullOrEmpty(subKey))
                    return Result<string>.Fail($"Invalid registry key path: {keyPath}");

                using (var existing = root.OpenSubKey(subKey, false))
                {
                    if (existing == null)
                        return Result<string>.Ok(string.Empty);
                }

                var backup = ExportKeyToBackup(keyPath);
                if (!backup.Success)
                    return Result<string>.Fail($"Backup failed, so the key was not deleted. {backup.ErrorMessage}");

                root.DeleteSubKeyTree(subKey, false);

                using (var check = root.OpenSubKey(subKey, false))
                {
                    if (check != null)
                        return Result<string>.Fail("The key could not be verified as deleted.");
                }

                Publish(new RegistryChange
                {
                    Operation = RegistryOperation.DeleteKeyTree,
                    KeyPath = keyPath,
                    KeyExisted = true,
                    BackupFile = backup.Data
                });

                Logger.Instance.Info("DeleteKeyTreeWithBackup", keyPath, "success",
                    backupCreated: "true", backupPath: backup.Data);
                return Result<string>.Ok(backup.Data!);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("DeleteKeyTreeWithBackup", keyPath, ex);
                return Result<string>.Fail(ex);
            }
        }

        #endregion

        #region Revert

        /// <summary>
        /// Precisely reverses a recorded change:
        /// restores the old value and kind, deletes values that did not exist before,
        /// and removes keys K-Win created (only while they are empty).
        /// The result is verified by reading the value back.
        /// </summary>
        public async Task<Result<bool>> RevertChangeAsync(RegistryChange change)
        {
            try
            {
                DevMode.ThrowIfReadOnly("registry revert");
                if (!Validator.IsValidRegistryPath(change.KeyPath, out string error))
                    return Result<bool>.Fail(error);

                if (change.Operation == RegistryOperation.DeleteKeyTree)
                {
                    if (string.IsNullOrEmpty(change.BackupFile))
                        return Result<bool>.Fail($"No backup recorded for deleted key {change.KeyPath}.");
                    var imported = await RestoreFromBackup(change.BackupFile);
                    if (!imported.Success) return imported;
                    var exists = KeyExists(change.KeyPath);
                    return exists.Success && exists.Data
                        ? Result<bool>.Ok(true)
                        : Result<bool>.Fail($"Key {change.KeyPath} was not restored.");
                }

                var (root, subKey) = ParseKeyPath(change.KeyPath);
                if (root == null)
                    return Result<bool>.Fail($"Invalid registry root in path: {change.KeyPath}");

                if (change.Before.Exists)
                {
                    using var key = root.CreateSubKey(subKey, true);
                    if (key == null)
                        return Result<bool>.Fail($"Could not open {change.KeyPath}");
                    var kind = RegistryValueCodec.WritableKind(change.Before.Kind);
                    key.SetValue(change.ValueName,
                        RegistryValueCodec.Decode(change.Before.Data ?? string.Empty, kind), kind);
                }
                else
                {
                    using (var key = root.OpenSubKey(subKey, true))
                    {
                        key?.DeleteValue(change.ValueName, false);
                    }

                    if (!string.IsNullOrEmpty(change.CreatedKeyRoot))
                        RemoveCreatedKeysIfEmpty(root, change.KeyPath, change.CreatedKeyRoot);
                }

                var now = ReadSnapshot(change.KeyPath, change.ValueName);
                if (!now.Success || !now.Data.Value.SameAs(change.Before))
                {
                    string got = now.Success ? now.Data.Value.ToString() : now.ErrorMessage ?? "";
                    return Result<bool>.Fail($"Revert of {change.KeyPath}\\{change.ValueName} could not be verified (expected {change.Before}, found {got}).");
                }

                Logger.Instance.Info("RevertChange", $"{change.KeyPath}\\{change.ValueName}", "success",
                    oldValue: change.After.ToString(), newValue: change.Before.ToString());
                return Result<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("RevertChange", $"{change.KeyPath}\\{change.ValueName}", ex);
                return Result<bool>.Fail(ex);
            }
        }

        /// <summary>
        /// Deletes keys from <paramref name="keyPath"/> upward to <paramref name="createdRoot"/>,
        /// stopping at the first key that still has values or subkeys.
        /// </summary>
        private static void RemoveCreatedKeysIfEmpty(RegistryKey root, string keyPath, string createdRoot)
        {
            string current = keyPath.TrimEnd('\\');
            string stop = createdRoot.TrimEnd('\\');
            if (!current.StartsWith(stop, StringComparison.OrdinalIgnoreCase))
                return;

            while (current.Length >= stop.Length)
            {
                var (_, sub) = ParseKeyPath(current);
                using (var key = root.OpenSubKey(sub, false))
                {
                    if (key == null) { /* already gone */ }
                    else if (key.ValueCount > 0 || key.SubKeyCount > 0) return;
                }
                try { root.DeleteSubKey(sub, false); } catch { return; }

                if (current.Equals(stop, StringComparison.OrdinalIgnoreCase)) return;
                int slash = current.LastIndexOf('\\');
                if (slash <= 0) return;
                current = current[..slash];
            }
        }

        /// <summary>Restores a registry state from a .reg backup file.</summary>
        public async Task<Result<bool>> RestoreFromBackup(string backupFilePath)
        {
            try
            {
                DevMode.ThrowIfReadOnly("registry import");
                if (!File.Exists(backupFilePath))
                    return Result<bool>.Fail($"Backup file not found: {backupFilePath}");

                var (exitCode, stderr, timedOut) = await RunRegExeAsync(new[] { "import", backupFilePath }, 30000);
                if (timedOut)
                    return Result<bool>.Fail("Registry restore timed out after 30 seconds.");

                if (exitCode == 0)
                {
                    Logger.Instance.Info("RestoreFromBackup", backupFilePath, "success");
                    return Result<bool>.Ok(true);
                }

                Logger.Instance.Error("RestoreFromBackup", backupFilePath, stderr);
                return Result<bool>.Fail($"reg.exe import failed (exit {exitCode}): {stderr.Trim()}");
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("RestoreFromBackup", backupFilePath, ex);
                return Result<bool>.Fail(ex);
            }
        }

        #endregion

        #region Helpers

        private Result<string> ReportFailure(string target, Result<string> result)
        {
            if (!result.Success)
            {
                try { ChangeFailed?.Invoke(target, result.ErrorMessage ?? "Unknown error"); }
                catch { /* never let a listener break the write path */ }
            }
            return result;
        }

        private void Publish(RegistryChange change)
        {
            try { ChangeRecorded?.Invoke(change); }
            catch (Exception ex) { Logger.Instance.Error("RegistryHelper.Publish", change.KeyPath, ex); }
        }

        /// <summary>
        /// Exports a key with reg.exe and verifies the exit code and the file on disk.
        /// </summary>
        internal Result<string> ExportKeyToBackup(string keyPath)
        {
            string safeName = string.Concat(keyPath.Select(c =>
                Path.GetInvalidFileNameChars().Contains(c) || c == '\\' ? '_' : c));
            if (safeName.Length > 100) safeName = safeName[..100];
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string backupFile = Path.Combine(_backupDirectory, $"{timestamp}_{Guid.NewGuid():N}"[..40] + $"_{safeName}.reg");

            try
            {
                var (exitCode, stderr, timedOut) = RunRegExeAsync(new[] { "export", keyPath, backupFile, "/y" }, 15000)
                    .GetAwaiter().GetResult();

                if (timedOut)
                    return Result<string>.Fail("reg.exe export timed out.");
                if (exitCode != 0)
                    return Result<string>.Fail($"reg.exe export failed (exit {exitCode}): {stderr.Trim()}");

                var info = new FileInfo(backupFile);
                if (!info.Exists || info.Length == 0)
                    return Result<string>.Fail("reg.exe reported success but the backup file is missing or empty.");

                return Result<string>.Ok(backupFile);
            }
            catch (Exception ex)
            {
                return Result<string>.Fail($"Backup export failed: {ex.Message}");
            }
        }

        private static async Task<(int ExitCode, string StdErr, bool TimedOut)> RunRegExeAsync(string[] args, int timeoutMs)
        {
            var psi = new ProcessStartInfo
            {
                FileName = RegExePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start reg.exe");
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(true); } catch { }
                return (-1, "timeout", true);
            }

            await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);
            return (proc.ExitCode, stderr, false);
        }

        /// <summary>Returns the full path of the highest key that does not exist yet.</summary>
        private static string? FindFirstMissingKey(RegistryKey root, string keyPath, string subKey)
        {
            string rootName = keyPath[..keyPath.IndexOf('\\')];
            var parts = subKey.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            string current = string.Empty;
            foreach (var part in parts)
            {
                current = current.Length == 0 ? part : $"{current}\\{part}";
                using var k = root.OpenSubKey(current, false);
                if (k == null)
                    return $"{rootName}\\{current}";
            }
            return null;
        }

        /// <summary>Parses a full registry path into root key and subkey components.</summary>
        internal static (RegistryKey? root, string subKey) ParseKeyPath(string keyPath)
        {
            int firstSlash = keyPath.IndexOf('\\');
            if (firstSlash < 0) return (null, string.Empty);

            string rootStr = keyPath[..firstSlash].ToUpperInvariant();
            string subKey = keyPath[(firstSlash + 1)..].TrimEnd('\\');

            RegistryKey? root = rootStr switch
            {
                "HKEY_CURRENT_USER" or "HKCU" => Registry.CurrentUser,
                "HKEY_LOCAL_MACHINE" or "HKLM" => Registry.LocalMachine,
                _ => null
            };

            return (root, subKey);
        }

        #endregion
    }
}
