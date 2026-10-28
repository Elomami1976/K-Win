using System.Runtime.InteropServices;

namespace KWin.Services
{
    /// <summary>
    /// Minimal, read-only wrapper over the Windows Performance Data Helper (pdh.dll) - the same
    /// counter source Task Manager and Performance Monitor use. No third-party library.
    /// English counter paths are used so it works on every Windows display language.
    /// Not thread-safe: one instance is used by one sampling thread.
    /// </summary>
    public sealed class PdhQuery : IDisposable
    {
        private IntPtr _query;
        private IntPtr _arrayBuffer;
        private uint _arrayBufferSize;

        private const uint PDH_FMT_DOUBLE = 0x00000200;
        private const uint PDH_FMT_NOCAP100 = 0x00008000;
        private const uint PDH_MORE_DATA = 0x800007D2;
        private const uint PDH_CSTATUS_VALID_DATA = 0x0;
        private const uint PDH_CSTATUS_NEW_DATA = 0x1;

        [StructLayout(LayoutKind.Explicit)]
        private struct PDH_FMT_COUNTERVALUE
        {
            [FieldOffset(0)] public uint CStatus;
            [FieldOffset(8)] public double DoubleValue;
        }

        // PDH_FMT_COUNTERVALUE_ITEM_W on x64: name pointer (8) + PDH_FMT_COUNTERVALUE (16)
        private const int ItemSize = 24;

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhAddEnglishCounterW(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

        [DllImport("pdh.dll")]
        private static extern uint PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll")]
        private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

        [DllImport("pdh.dll")]
        private static extern uint PdhCloseQuery(IntPtr query);

        private PdhQuery(IntPtr query) => _query = query;

        /// <summary>Opens a real-time query, or returns null if PDH is not available.</summary>
        public static PdhQuery? TryOpen()
        {
            try
            {
                return PdhOpenQueryW(null, IntPtr.Zero, out var q) == 0 ? new PdhQuery(q) : null;
            }
            catch
            {
                return null; // pdh.dll missing or blocked
            }
        }

        /// <summary>Adds a counter; returns null if this PC does not provide it.</summary>
        public IntPtr? TryAdd(string englishPath)
        {
            if (_query == IntPtr.Zero) return null;
            try
            {
                return PdhAddEnglishCounterW(_query, englishPath, IntPtr.Zero, out var c) == 0 ? c : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Collects new raw data for every counter. Rate counters need two collections.</summary>
        public bool Collect() => _query != IntPtr.Zero && PdhCollectQueryData(_query) == 0;

        /// <summary>Formatted value, or null when the counter has no valid data yet.</summary>
        public double? Read(IntPtr counter, bool allowAbove100 = false)
        {
            uint fmt = PDH_FMT_DOUBLE | (allowAbove100 ? PDH_FMT_NOCAP100 : 0);
            if (PdhGetFormattedCounterValue(counter, fmt, out _, out var v) != 0) return null;
            if (v.CStatus != PDH_CSTATUS_VALID_DATA && v.CStatus != PDH_CSTATUS_NEW_DATA) return null;
            return double.IsFinite(v.DoubleValue) ? v.DoubleValue : null;
        }

        /// <summary>
        /// Reads every instance of a wildcard counter (e.g. "\GPU Engine(*)\Utilization Percentage")
        /// into <paramref name="into"/>. Returns false when no data is available.
        /// The native buffer is reused between calls to avoid repeated allocations.
        /// </summary>
        public bool ReadArray(IntPtr counter, List<(string Instance, double Value)> into)
        {
            into.Clear();
            uint size = _arrayBufferSize;
            uint status = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE, ref size, out uint count, _arrayBuffer);
            if (status == PDH_MORE_DATA)
            {
                if (_arrayBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_arrayBuffer);
                _arrayBufferSize = size + 4096; // headroom: instances come and go with processes
                _arrayBuffer = Marshal.AllocHGlobal((int)_arrayBufferSize);
                size = _arrayBufferSize;
                status = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE, ref size, out count, _arrayBuffer);
            }
            if (status != 0 || count == 0) return false;

            for (int i = 0; i < count; i++)
            {
                IntPtr item = _arrayBuffer + i * ItemSize;
                uint cstatus = (uint)Marshal.ReadInt32(item, 8);
                if (cstatus != PDH_CSTATUS_VALID_DATA && cstatus != PDH_CSTATUS_NEW_DATA) continue;
                double value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16));
                string? name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item, 0));
                if (name != null && double.IsFinite(value)) into.Add((name, value));
            }
            return into.Count > 0;
        }

        public void Dispose()
        {
            if (_query != IntPtr.Zero)
            {
                PdhCloseQuery(_query);
                _query = IntPtr.Zero;
            }
            if (_arrayBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_arrayBuffer);
                _arrayBuffer = IntPtr.Zero;
                _arrayBufferSize = 0;
            }
        }
    }
}
