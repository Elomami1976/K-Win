using Microsoft.Win32;
using KWin.Models;
using KWin.Utils;

namespace KWin.Core
{
    /// <summary>
    /// Safe registry operations with mandatory backup before any write.
    /// All write operations export the target key to a .reg backup file first.
    /// Supports reading, writing, and restoring registry values.
    /// </summary>
    public class RegistryHelper
    {
        private readonly string _backupDirectory;

        public RegistryHelper()
        {
            _backupDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "K-win", "backups");
            Directory.CreateDirectory(_backupDirectory);
        }

        /// <summary>
        /// Reads a registry value. Returns null if the key or value doesn't exist.
        /// </summary>
        /// <param name="keyPath">Full registry path (e.g., HKCU\Software\...).</param>
        /// <param name="valueName">Name of the value to read.</param>
        /// <returns>Result containing the value or error.</returns>
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
                if (key == null)
                    return Result<object?>.Ok(null); // Key doesn't exist

                var value = key.GetValue(valueName);
                return Result<object?>.Ok(value);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("GetValue", $"{keyPath}\\{valueName}", ex);
                return Result<object?>.Fail(ex);
            }
        }

        /// <summary>
        /// Checks if a registry key exists.
        /// </summary>
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

        /// <summary>
        /// Sets a registry value with mandatory backup of the existing key first.
        /// This is the primary write method — ALWAYS creates a backup before modifying.
        /// </summary>
        /// <param name="keyPath">Full registry path.</param>
        /// <param name="valueName">Name of the value to set.</param>
        /// <param name="value">New value to write.</param>
        /// <param name="kind">Registry value type.</param>
        /// <returns>Result with the backup file path on success.</returns>
        public Result<string> SetValueWithBackup(string keyPath, string valueName, object value, RegistryValueKind kind)
        {
            try
            {
                if (!Validator.IsValidRegistryPath(keyPath, out string error))
                    return Result<string>.Fail(error);

                // Step 1: Get old value for logging
                var oldValueResult = GetValue(keyPath, valueName);
                string oldValueStr = oldValueResult.Success && oldValueResult.Data != null
                    ? oldValueResult.Data.ToString() ?? "(null)"
                    : "(not set)";

                // Step 2: Export key to backup BEFORE writing
                string backupPath = ExportKeyToBackup(keyPath);

                // Step 3: Write the new value
                var (root, subKey) = ParseKeyPath(keyPath);
                if (root == null)
                    return Result<string>.Fail($"Invalid registry root in path: {keyPath}");

                using var key = root.CreateSubKey(subKey, true);
                if (key == null)
                    return Result<string>.Fail($"Failed to open/create registry key: {keyPath}");

                key.SetValue(valueName, value, kind);

                // Step 4: Log the change
                Logger.Instance.Info("SetValueWithBackup",
                    $"{keyPath}\\{valueName}",
                    "success",
                    oldValue: oldValueStr,
                    newValue: value.ToString(),
                    backupCreated: "true",
                    backupPath: backupPath);

                return Result<string>.Ok(backupPath);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("SetValueWithBackup", $"{keyPath}\\{valueName}", ex);
                return Result<string>.Fail(ex);
            }
        }

        /// <summary>
        /// Deletes a registry value with mandatory backup first.
        /// </summary>
        public Result<string> DeleteValueWithBackup(string keyPath, string valueName)
        {
            try
            {
                if (!Validator.IsValidRegistryPath(keyPath, out string error))
                    return Result<string>.Fail(error);

                // Backup first
                string backupPath = ExportKeyToBackup(keyPath);

                var (root, subKey) = ParseKeyPath(keyPath);
                if (root == null)
                    return Result<string>.Fail($"Invalid registry root in path: {keyPath}");

                using var key = root.OpenSubKey(subKey, true);
                if (key == null)
                    return Result<string>.Ok(backupPath); // Key doesn't exist, nothing to delete

                key.DeleteValue(valueName, false);

                Logger.Instance.Info("DeleteValueWithBackup",
                    $"{keyPath}\\{valueName}",
                    "success",
                    backupCreated: "true",
                    backupPath: backupPath);

                return Result<string>.Ok(backupPath);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("DeleteValueWithBackup", $"{keyPath}\\{valueName}", ex);
                return Result<string>.Fail(ex);
            }
        }

        /// <summary>
        /// Restores a registry state from a .reg backup file.
        /// </summary>
        /// <param name="backupFilePath">Path to the .reg file to import.</param>
        /// <returns>Result indicating success or failure.</returns>
        public async Task<Result<bool>> RestoreFromBackup(string backupFilePath)
        {
            try
            {
                if (!File.Exists(backupFilePath))
                    return Result<bool>.Fail($"Backup file not found: {backupFilePath}");

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "reg.exe",
                    Arguments = $"import \"{backupFilePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null)
                    return Result<bool>.Fail("Failed to start reg.exe");

                using var cts = new CancellationTokenSource(30000);
                await proc.WaitForExitAsync(cts.Token);

                if (proc.ExitCode == 0)
                {
                    Logger.Instance.Info("RestoreFromBackup", backupFilePath, "success");
                    return Result<bool>.Ok(true);
                }

                string stderr = await proc.StandardError.ReadToEndAsync();
                Logger.Instance.Error("RestoreFromBackup", backupFilePath, stderr);
                return Result<bool>.Fail($"reg.exe import failed: {stderr}");
            }
            catch (OperationCanceledException)
            {
                Logger.Instance.Error("RestoreFromBackup", backupFilePath, "Timeout after 30s");
                return Result<bool>.Fail("Registry restore timed out after 30 seconds.");
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("RestoreFromBackup", backupFilePath, ex);
                return Result<bool>.Fail(ex);
            }
        }

        /// <summary>
        /// Gets all value names under a registry key.
        /// </summary>
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
                if (key == null)
                    return Result<string[]>.Ok(Array.Empty<string>());

                return Result<string[]>.Ok(key.GetValueNames());
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("GetValueNames", keyPath, ex);
                return Result<string[]>.Fail(ex);
            }
        }

        /// <summary>
        /// Gets all subkey names under a registry key.
        /// </summary>
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
                if (key == null)
                    return Result<string[]>.Ok(Array.Empty<string>());

                return Result<string[]>.Ok(key.GetSubKeyNames());
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("GetSubKeyNames", keyPath, ex);
                return Result<string[]>.Fail(ex);
            }
        }

        /// <summary>
        /// Exports a registry key to a timestamped .reg backup file using reg.exe.
        /// </summary>
        /// <param name="keyPath">Full registry path to export.</param>
        /// <returns>Full path to the created backup file.</returns>
        private string ExportKeyToBackup(string keyPath)
        {
            string safeName = keyPath.Replace("\\", "_").Replace("/", "_");
            if (safeName.Length > 100) safeName = safeName[..100];
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string backupFile = Path.Combine(_backupDirectory, $"{timestamp}_{safeName}.reg");

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "reg.exe",
                    Arguments = $"export \"{keyPath}\" \"{backupFile}\" /y",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                proc?.WaitForExit(10000); // 10s timeout for export
            }
            catch (Exception ex)
            {
                // Log but don't block the operation — backup is best-effort
                Logger.Instance.Warn("ExportKeyToBackup", keyPath,
                    $"Backup export failed: {ex.Message}");
            }

            return backupFile;
        }

        /// <summary>
        /// Parses a full registry path into root key and subkey components.
        /// </summary>
        private static (RegistryKey? root, string subKey) ParseKeyPath(string keyPath)
        {
            int firstSlash = keyPath.IndexOf('\\');
            if (firstSlash < 0) return (null, string.Empty);

            string rootStr = keyPath[..firstSlash].ToUpperInvariant();
            string subKey = keyPath[(firstSlash + 1)..];

            RegistryKey? root = rootStr switch
            {
                "HKEY_CURRENT_USER" or "HKCU" => Registry.CurrentUser,
                "HKEY_LOCAL_MACHINE" or "HKLM" => Registry.LocalMachine,
                "HKEY_CLASSES_ROOT" or "HKCR" => Registry.ClassesRoot,
                _ => null
            };

            return (root, subKey);
        }
    }
}
