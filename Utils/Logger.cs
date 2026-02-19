using System.Text.Json;
using KWin.Models;

namespace KWin.Utils
{
    /// <summary>
    /// Structured JSON Lines logger that writes to %AppData%\K-win\logs\.
    /// Thread-safe via lock. Automatically rotates by date.
    /// </summary>
    public sealed class Logger : IDisposable
    {
        private static readonly Lazy<Logger> _instance = new(() => new Logger());
        public static Logger Instance => _instance.Value;

        private readonly string _logDirectory;
        private readonly object _lock = new();
        private StreamWriter? _writer;
        private string _currentDate = string.Empty;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private Logger()
        {
            _logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "K-win", "logs");
            Directory.CreateDirectory(_logDirectory);
            EnsureWriter();
        }

        /// <summary>Gets the full path to today's log file.</summary>
        public string CurrentLogPath => Path.Combine(_logDirectory, $"kwin_{DateTime.Now:yyyy-MM-dd}.log");

        /// <summary>Gets the log directory path.</summary>
        public string LogDirectory => _logDirectory;

        /// <summary>Logs an informational operation entry.</summary>
        public void Info(string operation, string target, string status,
            string? oldValue = null, string? newValue = null,
            string? backupCreated = null, string? backupPath = null)
        {
            WriteLog(new ChangeLog
            {
                Level = "INFO",
                Operation = operation,
                Target = target,
                Status = status,
                OldValue = oldValue,
                NewValue = newValue,
                BackupCreated = backupCreated,
                BackupPath = backupPath
            });
        }

        /// <summary>Logs a warning entry.</summary>
        public void Warn(string operation, string target, string message)
        {
            WriteLog(new ChangeLog
            {
                Level = "WARN",
                Operation = operation,
                Target = target,
                Status = "warning",
                Error = message
            });
        }

        /// <summary>Logs an error entry.</summary>
        public void Error(string operation, string target, string error)
        {
            WriteLog(new ChangeLog
            {
                Level = "ERROR",
                Operation = operation,
                Target = target,
                Status = "failed",
                Error = error
            });
        }

        /// <summary>Logs an exception with full details.</summary>
        public void Error(string operation, string target, Exception ex)
        {
            Error(operation, target, $"{ex.GetType().Name}: {ex.Message}");
        }

        /// <summary>Writes a ChangeLog entry as a JSON line.</summary>
        public void WriteLog(ChangeLog entry)
        {
            lock (_lock)
            {
                try
                {
                    EnsureWriter();
                    string json = JsonSerializer.Serialize(entry, _jsonOptions);
                    _writer?.WriteLine(json);
                    _writer?.Flush();
                }
                catch
                {
                    // Logging should never throw
                }
            }
        }

        /// <summary>Reads all log entries from today's log file.</summary>
        public List<ChangeLog> ReadTodayLogs()
        {
            var logs = new List<ChangeLog>();
            string path = CurrentLogPath;

            if (!File.Exists(path))
                return logs;

            try
            {
                // Read with sharing allowed so we don't conflict with the writer
                using var reader = new StreamReader(
                    new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var entry = JsonSerializer.Deserialize<ChangeLog>(line, _jsonOptions);
                        if (entry != null) logs.Add(entry);
                    }
                    catch { /* Skip malformed lines */ }
                }
            }
            catch { /* If we can't read, return empty */ }

            return logs;
        }

        /// <summary>Cleans up log files older than the specified number of days.</summary>
        public int CleanOldLogs(int daysToKeep = 30)
        {
            int cleaned = 0;
            try
            {
                var cutoff = DateTime.Now.AddDays(-daysToKeep);
                foreach (var file in Directory.GetFiles(_logDirectory, "kwin_*.log"))
                {
                    if (File.GetCreationTime(file) < cutoff)
                    {
                        File.Delete(file);
                        cleaned++;
                    }
                }
            }
            catch { /* Best effort */ }
            return cleaned;
        }

        private void EnsureWriter()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (_currentDate == today && _writer != null) return;

            _writer?.Dispose();
            _currentDate = today;

            string path = Path.Combine(_logDirectory, $"kwin_{today}.log");
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(stream) { AutoFlush = false };
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }
    }
}
