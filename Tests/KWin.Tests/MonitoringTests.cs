using KWin.Services;
using Xunit;

namespace KWin.Tests
{
    public class MetricHistoryTests
    {
        [Fact]
        public void KeepsNewestValues_OldestFirst()
        {
            var h = new MetricHistory(3);
            for (int i = 1; i <= 5; i++) h.Add(i);
            var buf = new double[3];
            Assert.Equal(3, h.CopyTo(buf));
            Assert.Equal(new double[] { 3, 4, 5 }, buf);
        }

        [Fact]
        public void PartialFill_AndSmallerDestination()
        {
            var h = new MetricHistory(5);
            h.Add(10); h.Add(20); h.Add(30);
            var all = new double[5];
            Assert.Equal(3, h.CopyTo(all));
            Assert.Equal(new double[] { 10, 20, 30 }, all[..3]);

            var last2 = new double[2];
            Assert.Equal(2, h.CopyTo(last2));
            Assert.Equal(new double[] { 20, 30 }, last2); // newest values kept
        }

        [Fact]
        public void MissingMeasurement_IsAGap_NotZero()
        {
            var h = new MetricHistory(3);
            h.Add(50); h.Add(null); h.Add(60);
            var buf = new double[3];
            h.CopyTo(buf);
            Assert.Equal(50, buf[0]);
            Assert.True(double.IsNaN(buf[1]));
            Assert.Equal(60, buf[2]);
        }

        [Fact]
        public void Clear_Empties()
        {
            var h = new MetricHistory(3);
            h.Add(1);
            h.Clear();
            Assert.Equal(0, h.Count);
        }
    }

    public class GpuAggregatorTests
    {
        private static readonly Dictionary<string, double> Scratch = new(StringComparer.OrdinalIgnoreCase);

        [Fact]
        public void SumsPerEngineType_AndReportsBusiestEngine()
        {
            var engines = new List<(string, double)>
            {
                ("pid_100_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 20),
                ("pid_200_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 15),
                ("pid_300_luid_0x0_0x1_phys_0_eng_4_engtype_VideoDecode", 30),
                ("pid_100_luid_0x0_0x1_phys_0_eng_2_engtype_Copy", 5),
            };
            Assert.Equal(35, GpuAggregator.Aggregate(engines, Scratch));
        }

        [Fact]
        public void IsClampedTo100()
        {
            var engines = new List<(string, double)>
            {
                ("pid_1_engtype_3D", 80), ("pid_2_engtype_3D", 70)
            };
            Assert.Equal(100, GpuAggregator.Aggregate(engines, Scratch));
        }

        [Fact]
        public void NoEngines_IsUnavailable_IdleIsZero()
        {
            Assert.Null(GpuAggregator.Aggregate(new List<(string, double)>(), Scratch));
            Assert.Equal(0, GpuAggregator.Aggregate(new List<(string, double)> { ("pid_1_engtype_3D", 0) }, Scratch));
        }
    }

    public class SystemMonitorServiceTests
    {
        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        [InlineData(4, 3)]   // nearest allowed (3 and 5 tie -> first)
        [InlineData(0, 1)]
        [InlineData(60, 10)]
        public void Interval_IsLimitedToAllowedValues(int requested, int expected)
        {
            Assert.Equal(expected, SystemMonitorService.NormalizeInterval(requested));
        }

        [Fact]
        public async Task ProducesRealSamples_InValidRanges_AndStopsOnDispose()
        {
            var samples = new List<MonitorSample>();
            var monitor = new SystemMonitorService(intervalSeconds: 1);
            monitor.SampleReady += s => { lock (samples) samples.Add(s); };
            monitor.Start();

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                lock (samples) if (samples.Count >= 3) break;
                await Task.Delay(200);
            }

            MonitorSample last;
            lock (samples)
            {
                Assert.True(samples.Count >= 3, "expected at least 3 samples");
                last = samples[^1];
            }

            // CPU is always measurable (GetSystemTimes fallback) once a baseline exists.
            Assert.NotNull(last.CpuPercent);
            Assert.InRange(last.CpuPercent!.Value, 0, 100);

            // Memory: real and self-consistent.
            Assert.NotNull(last.MemoryTotalBytes);
            Assert.Equal(last.MemoryTotalBytes, last.MemoryUsedBytes + last.MemoryAvailableBytes);
            Assert.InRange(last.MemoryPercent!.Value, 0, 100);

            // System drive capacity.
            Assert.NotNull(last.DiskTotalBytes);
            Assert.InRange(last.DiskUsedPercent!.Value, 0, 100);

            // Optional counters: either unavailable (null) or a valid percentage - never out of range.
            if (last.DiskActivePercent is double d) Assert.InRange(d, 0, 100);
            if (last.GpuPercent is double g) Assert.InRange(g, 0, 100);
            if (last.CpuCurrentMhz is double mhz) Assert.InRange(mhz, 100, 10000);

            Assert.True(monitor.CpuHistory.Count >= 3);

            monitor.Dispose();
            int countAfterDispose;
            lock (samples) countAfterDispose = samples.Count;
            await Task.Delay(2500);
            lock (samples) Assert.Equal(countAfterDispose, samples.Count);
        }

        [Fact]
        public async Task Paused_TakesNoSamples()
        {
            int count = 0;
            using var monitor = new SystemMonitorService(intervalSeconds: 1);
            monitor.SampleReady += _ => Interlocked.Increment(ref count);
            monitor.Start();
            await Task.Delay(1500);

            monitor.SetPaused(true);
            await Task.Delay(300); // let an in-flight sample finish
            int pausedAt = Volatile.Read(ref count);
            await Task.Delay(3000);
            Assert.Equal(pausedAt, Volatile.Read(ref count));

            monitor.SetPaused(false); // resuming samples right away
            await Task.Delay(800);
            Assert.True(Volatile.Read(ref count) > pausedAt);
        }

        [Fact]
        public void PdhQuery_UnknownCounter_IsReportedAsUnavailable()
        {
            using var q = PdhQuery.TryOpen();
            Assert.NotNull(q);
            Assert.Null(q!.TryAdd(@"\This Counter(Does Not)\Exist"));
            Assert.NotNull(q.TryAdd(@"\Memory\Available Bytes"));
            Assert.True(q.Collect());
        }
    }
}
