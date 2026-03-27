using System.Runtime.InteropServices;

namespace musicApp.Helpers;

/// <summary>Point-in-time physical memory and system CPU (from two short-spaced samples).</summary>
public readonly record struct SystemResourceSnapshot(
    ulong TotalRamBytes,
    ulong AvailableRamBytes,
    double CpuBusyPercent,
    double CpuAvailablePercent);

public static class WindowsSystemMetrics
{
    public static SystemResourceSnapshot Sample(TimeSpan cpuSampleInterval)
    {
        var (total, avail) = TryGetPhysicalMemory(out var t, out var a) ? (t, a) : (0UL, 0UL);
        var cpuBusy = TrySampleCpuBusyPercent(cpuSampleInterval);
        var cpuAvail = Math.Clamp(100.0 - cpuBusy, 0.0, 100.0);
        return new SystemResourceSnapshot(total, avail, cpuBusy, cpuAvail);
    }

    private static bool TryGetPhysicalMemory(out ulong totalPhys, out ulong availPhys)
    {
        totalPhys = availPhys = 0;
        var st = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref st))
            return false;
        totalPhys = st.ullTotalPhys;
        availPhys = st.ullAvailPhys;
        return true;
    }

    /// <summary>System-wide busy % over ~<paramref name="interval"/> (two GetSystemTimes samples).</summary>
    private static double TrySampleCpuBusyPercent(TimeSpan interval)
    {
        if (!GetSystemTimes(out var idle1, out var k1, out var u1))
            return 0;
        Thread.Sleep(interval <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(100) : interval);
        if (!GetSystemTimes(out var idle2, out var k2, out var u2))
            return 0;

        var idleDelta = SubtractFileTime(idle2, idle1);
        var totalDelta = SubtractFileTime(k2, k1) + SubtractFileTime(u2, u1);
        if (totalDelta == 0)
            return 0;
        var busy = 100.0 * (1.0 - (double)idleDelta / totalDelta);
        return double.IsFinite(busy) ? Math.Clamp(busy, 0, 100) : 0;
    }

    private static ulong SubtractFileTime(FILETIME a, FILETIME b)
    {
        return ToUInt64(a) - ToUInt64(b);
    }

    private static ulong ToUInt64(FILETIME ft) => ((ulong)ft.dwHighDateTime << 32) | ft.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);
}
