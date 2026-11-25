using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace musicApp.Helpers;

/// <summary>
/// Work area (screen minus taskbar) of the monitor a rectangle is on, in WPF DIPs.
/// Monitor APIs use device pixels; WPF window bounds are DIPs, so both directions are converted.
/// </summary>
internal static class MonitorWorkArea
{
    private const uint MonitorDefaultToNearest = 2;

    /// <summary>False when <paramref name="visual"/> is not shown yet (no DPI to convert with).</summary>
    public static bool TryGet(Visual visual, Rect boundsDips, out Rect workAreaDips)
    {
        workAreaDips = default;
        if (PresentationSource.FromVisual(visual)?.CompositionTarget is not { } target)
            return false;

        var boundsPx = Rect.Transform(boundsDips, target.TransformToDevice);
        var rect = new RECT
        {
            Left = (int)Math.Round(boundsPx.Left),
            Top = (int)Math.Round(boundsPx.Top),
            Right = (int)Math.Round(boundsPx.Right),
            Bottom = (int)Math.Round(boundsPx.Bottom)
        };
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromRect(ref rect, MonitorDefaultToNearest), ref info))
            return false;

        var workPx = new Rect(info.rcWork.Left, info.rcWork.Top,
            info.rcWork.Right - info.rcWork.Left, info.rcWork.Bottom - info.rcWork.Top);
        workAreaDips = Rect.Transform(workPx, target.TransformFromDevice);
        return true;
    }

    /// <summary>True when any part of the bounds is on a monitor's work area.</summary>
    public static bool IsOnScreen(Visual visual, double left, double top, double width, double height)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(width) || !double.IsFinite(height))
            return false;
        var bounds = new Rect(left, top, Math.Max(width, 1), Math.Max(height, 1));
        return TryGet(visual, bounds, out var work) && work.IntersectsWith(bounds);
    }

    /// <summary>
    /// Moves and shrinks saved bounds so the whole window sits on the nearest monitor's work area.
    /// False when the bounds are not finite or <paramref name="visual"/> has no window handle yet.
    /// </summary>
    public static bool TryFit(Visual visual, double left, double top, double width, double height,
        Size minSize, out Rect fitted)
    {
        fitted = default;
        if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(width) || !double.IsFinite(height))
            return false;

        width = Math.Max(width, minSize.Width);
        height = Math.Max(height, minSize.Height);
        if (!TryGet(visual, new Rect(left, top, width, height), out var work))
            return false;

        width = Math.Min(width, work.Width);
        height = Math.Min(height, work.Height);
        // Fully off this screen (monitor gone, garbage values): center it instead of hugging the nearest edge.
        if (!work.IntersectsWith(new Rect(left, top, width, height)))
        {
            left = work.Left + (work.Width - width) / 2;
            top = work.Top + (work.Height - height) / 2;
        }
        // Clamp the offset, not the edge: work.Right - work.Width can round below work.Left and make Clamp throw.
        fitted = new Rect(
            work.Left + Math.Clamp(left - work.Left, 0, work.Width - width),
            work.Top + Math.Clamp(top - work.Top, 0, work.Height - height),
            width, height);
        return true;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }
}
