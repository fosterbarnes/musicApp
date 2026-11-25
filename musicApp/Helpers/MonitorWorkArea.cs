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
