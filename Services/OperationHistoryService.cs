using System.Text.Json;
using KWin.Models;
using KWin.Utils;

namespace KWin.Services
{
    /// <summary>
    /// Keeps a saved history of every operation K-Win performed (history.json in the K-win data folder).
    /// Thread-safe. The newest entries are kept when the cap is reached.
    /// </summary>
    public class OperationHistoryService
    {
        public const int MaxEntries = 500;

        private readonly string _path;
        private readonly object _lock = new();
        private readonly List<OperationRecord> _records;

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public event Action? Changed;

        public OperationHistoryService(string? dataDirectory = null)
        {
            string dir = dataDirectory ?? AppPaths.DataDirectory;
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "history.json");
            _records = Load();
        }

        public string FilePath => _path;

        public void Add(OperationRecord record)
        {
            lock (_lock)
            {
                _records.Add(record);
                if (_records.Count > MaxEntries)
                    _records.RemoveRange(0, _records.Count - MaxEntries);
                Save();
            }
            Changed?.Invoke();
        }

        /// <summary>Most recent first.</summary>
        public IReadOnlyList<OperationRecord> GetRecent(int count = 20)
        {
            lock (_lock)
            {
                return _records.AsEnumerable().Reverse().Take(count).ToList();
            }
        }

        public OperationRecord? LastSuccessful()
        {
            lock (_lock)
            {
                return _records.LastOrDefault(r => r.Status == OperationStatus.Completed);
            }
        }

        public int Count
        {
            get { lock (_lock) return _records.Count; }
        }

        private List<OperationRecord> Load()
        {
            try
            {
                if (!File.Exists(_path)) return new List<OperationRecord>();
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<List<OperationRecord>>(json, JsonOptions) ?? new List<OperationRecord>();
            }
            catch (Exception ex)
            {
                // Keep the unreadable file for inspection instead of overwriting it.
                Logger.Instance.Error("OperationHistory.Load", _path, ex);
                AppPaths.QuarantineCorruptFile(_path);
                return new List<OperationRecord>();
            }
        }

        private void Save()
        {
            try
            {
                AppPaths.WriteAllTextAtomic(_path, JsonSerializer.Serialize(_records, JsonOptions));
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("OperationHistory.Save", _path, ex);
            }
        }
    }
}
