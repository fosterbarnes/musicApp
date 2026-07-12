using System;

namespace musicApp.Helpers;

public static class SeekBarInteractionHelper
{
    public static double Clamp(double value, double min, double max) =>
        Math.Max(min, Math.Min(max, value));

    public static TimeSpan TimeFromSeekX(double x, double barWidth, TimeSpan totalDuration)
    {
        if (barWidth <= 0 || totalDuration.TotalSeconds <= 0)
            return TimeSpan.Zero;
        double progress = Clamp(x, 0, barWidth) / barWidth;
        return TimeSpan.FromSeconds(progress * totalDuration.TotalSeconds);
    }

    public static double ProgressFillWidth(TimeSpan current, TimeSpan total, double barWidth)
    {
        if (total.TotalSeconds <= 0 || barWidth <= 0)
            return 0;
        double progress = current.TotalSeconds / total.TotalSeconds;
        return Math.Max(0, Math.Min(barWidth, barWidth * progress));
    }

    public static bool HasPassedMoveDelay(DateTime mouseDownUtc, int delayMs) =>
        (DateTime.Now - mouseDownUtc).TotalMilliseconds >= delayMs;

    public static bool IsWithinDragTolerance(double x, double barWidth, double tolerance) =>
        x >= -tolerance && x <= barWidth + tolerance;
}
