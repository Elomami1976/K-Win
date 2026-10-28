using Microsoft.Win32;
using KWin.Core;
using KWin.Utils;

namespace KWin.Services
{
    /// <summary>One set of measurements. Null means "could not be measured" (shown as Unavailable).</summary>
    public sealed class MonitorSample
    {
        public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

        public double? CpuPercent { get; init; }
        /// <summary>Current effective clock (base clock x "% Processor Performance").</summary>
        public double? CpuCurrentMhz { get; init; }
        public double? CpuBaseMhz { get; init; }

        public ulong? MemoryTotalBytes { get; init; }
        public ulong? MemoryUsedBytes { get; init; }
        public ulong? MemoryAvailableBytes { get; init; }
        public double? MemoryPercent { get; init; }

        public string SystemDrive { get; init; } = @"C:\";
        public long? DiskTotalBytes { get; init; }
        public long? DiskUsedBytes { get; init; }
        public double? DiskUsedPercent { get; init; }
        /// <summary>Share of time any physical disk was busy (100 - "% Idle Time").</summary>
        public double? DiskActivePercent { get; init; }

        public double? GpuPercent { get; init; }
    }

    /// <summary>
    /// Fixed-size history of one metric (oldest to newest). Missing measurements are stored as NaN
    /// and drawn as gaps. Thread-safe; no allocation per sample.
    /// </summary>
    public sealed class MetricHistory
    {
        private readonly double[] _values;
        private readonly object _lock = new();
        private int _next;
        private int _count;

        public MetricHistory(int capacity)
        {
            if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
            _values = new double[capacity];
        }

        public int Capacity => _values.Length;

        public int Count { get { lock (_lock) return _count; } }

        public void Add(double? value)
        {
            lock (_lock)
            {
                _values[_next] = value ?? double.NaN;
                _next = (_next + 1) % _values.Length;
                if (_count < _values.Length) _count++;
            }
        }

        /// <summary>Copies the values oldest-first into <paramref name="destination"/>; returns how many were copied.</summary>
        public int CopyTo(Span<double> destination)
        {
            lock (_lock)
            {
                int n = Math.Min(_count, destination.Length);
                int start = (_next - _count + _values.Length) % _values.Length + (_count - n);
                for (int i = 0; i < n; i++)
                    destination[i] = _values[(start + i) % _values.Length];
                return n;
            }
        }

        public void Clear()
        {
            lock (_lock) { _next = 0; _count = 0; }
        }
    }

    /// <summary>
    /// GPU utilization the way Task Manager reports it: utilization is summed per engine type
    /// (3D, Copy, VideoDecode, ...) across all processes, and the busiest engine type is the GPU's usage.
    /// </summary>
    public static class GpuAggregator
    {
        public static double? Aggregate(IReadOnlyList<(string Instance, double Value)> engines, Dictionary<string, double> scratch)
        {
            if (engines.Count == 0) return null;
            scratch.Clear();
            foreach (var (instance, value) in engines)
            {
                if (value <= 0) continue;
                int i = instance.IndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
                string type = i >= 0 ? instance[(i + 8)..] : "unknown";
                scratch[type] = scratch.TryGetValue(type, out var sum) ? sum + value : value;
            }
            double max = 0;
            foreach (var v in scratch.Values) if (v > max) max = v;
            return Math.Clamp(max, 0, 100);
        }
    }

    /// <summary>
    /// Read-only, lightweight system monitor. Samples on a background thread at a configurable
    /// interval, keeps short histories for charts, and does nothing while paused (window minimized
    /// or dashboard hidden). Every value comes from a Windows API or performance counter; values that
    /// can't be read are reported as null - never estimated.
    /// </summary>
    public sealed class SystemMonitorService : IDisposable
    {
        public const int HistoryLength = 60;
        public static readonly int[] AllowedIntervalsSeconds = { 1, 2, 3, 5, 10 };

        private readonly object _sampleLock = new();
        private readonly CpuUsageSampler _cpuFallback = new();
        private readonly List<(string, double)> _gpuItems = new();
        private readonly Dictionary<string, double> _gpuScratch = new(StringComparer.OrdinalIgnoreCase);
        private readonly double? _baseMhz;

        private PdhQuery? _pdh;
        private IntPtr? _cpuUtility, _cpuPerformance, _diskIdle, _gpuEngines;
        private bool _countersReady;

        private PeriodicTimer? _timer;
        private CancellationTokenSource? _cts;
        private Task? _loop;
        private volatile bool _paused;
        private bool _disposed;

        public event Action<MonitorSample>? SampleReady;

        public MetricHistory CpuHistory { get; } = new(HistoryLength);
        public MetricHistory MemoryHistory { get; } = new(HistoryLength);
        public MetricHistory DiskHistory { get; } = new(HistoryLength);
        public MetricHistory GpuHistory { get; } = new(HistoryLength);

        public TimeSpan Interval { get; private set; }
        public bool IsPaused => _paused;
        public MonitorSample? Latest { get; private set; }

        /// <summary>Which optional counters this PC provides (for diagnostics and the report).</summary>
        public bool HasGpuCounters => _gpuEngines != null;
        public bool HasCpuFrequencyCounter => _cpuPerformance != null && _baseMhz != null;
        public bool HasDiskActivityCounter => _diskIdle != null;

        public SystemMonitorService(int intervalSeconds = 2)
        {
            Interval = TimeSpan.FromSeconds(NormalizeInterval(intervalSeconds));
            _baseMhz = ReadBaseMhz();
        }

        public static int NormalizeInterval(int seconds) =>
            AllowedIntervalsSeconds.Contains(seconds) ? seconds
            : AllowedIntervalsSeconds.OrderBy(s => Math.Abs(s - seconds)).First();

        /// <summary>Starts the background sampling loop (idempotent).</summary>
        public void Start()
        {
            if (_disposed || _loop != null) return;
            _cts = new CancellationTokenSource();
            _timer = new PeriodicTimer(Interval);
            var token = _cts.Token;
            _loop = Task.Run(async () =>
            {
                try
                {
                    InitCounters();
                    SampleSafe();
                    while (await _timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                    {
                        if (!_paused) SampleSafe();
                    }
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
                catch (Exception ex) { Logger.Instance.Error("SystemMonitor", "loop", ex); }
            });
        }

        public void SetInterval(int seconds)
        {
            Interval = TimeSpan.FromSeconds(NormalizeInterval(seconds));
            try { if (_timer != null) _timer.Period = Interval; }
            catch (ObjectDisposedException) { }
        }

        /// <summary>Pausing stops all sampling work. Resuming takes a fresh sample right away.</summary>
        public void SetPaused(bool paused)
        {
            if (_paused == paused) return;
            _paused = paused;
            if (!paused && _loop != null && !_disposed)
                Task.Run(SampleSafe);
        }

        private void InitCounters()
        {
            _pdh = PdhQuery.TryOpen();
            if (_pdh == null) return;
            // Same counters Task Manager uses (Windows 8 and later).
            _cpuUtility = _pdh.TryAdd(@"\Processor Information(_Total)\% Processor Utility");
            _cpuPerformance = _pdh.TryAdd(@"\Processor Information(_Total)\% Processor Performance");
            _diskIdle = _pdh.TryAdd(@"\PhysicalDisk(_Total)\% Idle Time");
            _gpuEngines = _pdh.TryAdd(@"\GPU Engine(*)\Utilization Percentage");
            _pdh.Collect(); // baseline for rate counters
            _cpuFallback.Sample();
            Logger.Instance.Info("SystemMonitor", "counters", "ready",
                newValue: $"cpuUtility={_cpuUtility != null} cpuPerf={_cpuPerformance != null} baseMhz={_baseMhz?.ToString() ?? "n/a"} diskIdle={_diskIdle != null} gpu={_gpuEngines != null}");
        }

        private void SampleSafe()
        {
            if (!Monitor.TryEnter(_sampleLock)) return; // never overlap samples
            try
            {
                if (_disposed) return;
                var s = TakeSample();
                Latest = s;
                CpuHistory.Add(s.CpuPercent);
                MemoryHistory.Add(s.MemoryPercent);
                DiskHistory.Add(s.DiskActivePercent);
                GpuHistory.Add(s.GpuPercent);
                SampleReady?.Invoke(s);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("SystemMonitor", "sample", ex);
            }
            finally
            {
                Monitor.Exit(_sampleLock);
            }
        }

        private MonitorSample TakeSample()
        {
            bool collected = _pdh?.Collect() ?? false;
            _countersReady |= collected;

            // CPU: "% Processor Utility" (matches Task Manager); falls back to GetSystemTimes.
            double? fallbackCpu = _cpuFallback.Sample();
            double? cpu = null;
            if (collected && _cpuUtility is IntPtr cu)
                cpu = _pdh!.Read(cu, allowAbove100: true) is double v ? Math.Clamp(v, 0, 100) : null;
            cpu ??= fallbackCpu;

            double? mhz = null;
            if (collected && _cpuPerformance is IntPtr cp && _baseMhz is double baseMhz)
                mhz = _pdh!.Read(cp, allowAbove100: true) is double perf && perf > 0 ? baseMhz * perf / 100.0 : null;

            // Memory: GlobalMemoryStatusEx.
            ulong? total = null, used = null, avail = null;
            double? memPct = null;
            var mem = MemoryManager.GetMemoryInfo();
            if (mem.Success && mem.Data!.TotalPhysicalMB > 0)
            {
                total = mem.Data.TotalPhysicalMB * 1048576UL;
                avail = mem.Data.AvailablePhysicalMB * 1048576UL;
                used = total - avail;
                memPct = used * 100.0 / total;
            }

            // Disk: capacity of the system drive (DriveInfo) + activity of all physical disks (PDH).
            string root = SystemInfoService.SystemDriveRoot;
            long? dTotal = null, dUsed = null;
            double? dPct = null;
            try
            {
                var d = new DriveInfo(root);
                if (d.IsReady && d.TotalSize > 0)
                {
                    dTotal = d.TotalSize;
                    dUsed = d.TotalSize - d.TotalFreeSpace;
                    dPct = dUsed * 100.0 / dTotal;
                }
            }
            catch { /* unavailable */ }

            double? diskActive = null;
            if (collected && _diskIdle is IntPtr di)
                diskActive = _pdh!.Read(di) is double idle ? Math.Clamp(100 - idle, 0, 100) : null;

            // GPU: "GPU Engine" counters (WDDM 2.0+ drivers). Unavailable when not provided.
            double? gpu = null;
            if (collected && _gpuEngines is IntPtr ge && _pdh!.ReadArray(ge, _gpuItems))
                gpu = GpuAggregator.Aggregate(_gpuItems, _gpuScratch);

            return new MonitorSample
            {
                CpuPercent = cpu,
                CpuCurrentMhz = mhz,
                CpuBaseMhz = _baseMhz,
                MemoryTotalBytes = total,
                MemoryUsedBytes = used,
                MemoryAvailableBytes = avail,
                MemoryPercent = memPct,
                SystemDrive = root,
                DiskTotalBytes = dTotal,
                DiskUsedBytes = dUsed,
                DiskUsedPercent = dPct,
                DiskActivePercent = diskActive,
                GpuPercent = gpu
            };
        }

        private static double? ReadBaseMhz()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                return key?.GetValue("~MHz") is int mhz && mhz > 0 ? mhz : null;
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SampleReady = null;
            try { _cts?.Cancel(); } catch { }
            try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _timer?.Dispose();
            _cts?.Dispose();
            lock (_sampleLock)
            {
                _pdh?.Dispose();
                _pdh = null;
            }
        }
    }
}
