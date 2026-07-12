using System;
using System.Runtime.InteropServices;
using musicApp.Constants;

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

/// <summary>Maps system resource snapshots to parallel scan degree (smoothed across batches).</summary>
public static class ScanConcurrencyAdvisor
{
    private const int MaxParallelismConservative = 8;
    private const int MaxParallelismAggressive = 16;
    private const double AvailableRamFractionComfort = 0.12;
    private const ulong AbsoluteMinAvailableRamBytes = 512UL * 1024 * 1024;
    private const double CpuBusyHigh = 88.0;
    private const double CpuBusyLow = 48.0;
    private const double CpuGreenZoneMax = 54.0;

    public static int Recommend(
        SystemResourceSnapshot snapshot,
        int processorCount,
        ref int previousSmoothedDop)
    {
        var totalRam = snapshot.TotalRamBytes;
        var availRam = snapshot.AvailableRamBytes;
        var memComfort = totalRam == 0
            ? true
            : availRam >= AbsoluteMinAvailableRamBytes &&
              (double)availRam / totalRam >= AvailableRamFractionComfort;

        var tierMax = memComfort && snapshot.CpuBusyPercent <= CpuGreenZoneMax
            ? MaxParallelismAggressive
            : MaxParallelismConservative;

        // Reserve one logical CPU for the UI thread so the window stays responsive during scans.
        var reserved = Math.Max(1, processorCount - 1);
        var maxCap = Math.Clamp(reserved, 1, tierMax);
        var raw = maxCap;

        if (!memComfort)
            raw = Math.Max(1, raw / 2);

        if (snapshot.CpuBusyPercent >= CpuBusyHigh)
            raw = Math.Max(1, raw / 2);
        else if (snapshot.CpuBusyPercent <= CpuBusyLow && memComfort)
            raw = maxCap;

        raw = Math.Clamp(raw, 1, maxCap);

        if (previousSmoothedDop <= 0)
            previousSmoothedDop = raw;
        else
            previousSmoothedDop = Math.Max(1, (raw + previousSmoothedDop + 1) / 2);

        return previousSmoothedDop;
    }
}

/// <summary>Maps scan-style resource advice to album grid UI append batch sizes.</summary>
public static class AlbumRebuildUiAdvisor
{
    public static int ItemsPerDispatcherBatch(int smoothedDopFromScanAdvisor)
    {
        int raw = smoothedDopFromScanAdvisor * UILayoutConstants.AlbumRebuildBatchDopScale;
        return Math.Clamp(raw, UILayoutConstants.AlbumRebuildBatchMin, UILayoutConstants.AlbumRebuildBatchMax);
    }

    public static int PrefixPhaseItemsPerBatch(int baseBatch)
    {
        int boosted = baseBatch * UILayoutConstants.AlbumRebuildPrefixBatchMultiplier;
        return Math.Min(boosted, UILayoutConstants.AlbumRebuildPrefixMaxBatch);
    }
}
