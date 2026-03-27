using System.Diagnostics;

namespace musicApp.Helpers;

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

        // Never exceed logical CPU count; Aggressive constant is only an upper bound.
        var maxCap = Math.Clamp(processorCount, 1, tierMax);
        var raw = maxCap;

        if (!memComfort)
            raw = Math.Max(1, raw / 2);

        if (snapshot.CpuBusyPercent >= CpuBusyHigh)
            raw = Math.Max(1, raw / 2);
        else if (snapshot.CpuBusyPercent <= CpuBusyLow && memComfort)
            raw = maxCap;

        raw = Math.Clamp(raw, 1, maxCap);

        var prevSmoothed = previousSmoothedDop;
        if (previousSmoothedDop <= 0)
            previousSmoothedDop = raw;
        else
            previousSmoothedDop = Math.Max(1, (raw + previousSmoothedDop + 1) / 2);

        var speedMode = tierMax == MaxParallelismAggressive
            ? $"High throughput (idle PC): policy allows up to {tierMax} parallel; " +
              $"this machine has {processorCount} logical CPUs so effective ceiling is {maxCap}."
            : $"Throttled (busy/tight RAM): policy allows up to {tierMax} parallel; " +
              $"effective ceiling is {maxCap} ({processorCount} logical CPUs).";
        var ramGb = totalRam == 0
            ? "RAM unknown"
            : $"{availRam / (1024.0 * 1024.0):F1} GB free of {totalRam / (1024.0 * 1024.0):F1} GB total";
        string workerLine = prevSmoothed <= 0
            ? $"Parallel worker target is now {previousSmoothedDop}."
            : prevSmoothed == previousSmoothedDop
                ? $"Keeping {previousSmoothedDop} parallel workers."
                : $"Adjusting workers: was {prevSmoothed}, now {previousSmoothedDop} (this step wanted ~{raw}).";
        Debug.WriteLine($"[LibraryScan] {speedMode} — {ramGb}, CPU ~{snapshot.CpuBusyPercent:F0}% busy. {workerLine}");

        return previousSmoothedDop;
    }
}
