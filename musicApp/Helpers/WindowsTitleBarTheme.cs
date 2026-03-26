using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace musicApp.Helpers;

public static class WindowsTitleBarTheme
{
    private const int DwmWindowAttributeUseImmersiveDarkMode = 20;
    private const int DwmWindowAttributeUseImmersiveDarkModeBefore20 = 19;

    public static void ApplyImmersiveDarkMode(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        int enabled = 1;
        _ = DwmSetWindowAttribute(hwnd, DwmWindowAttributeUseImmersiveDarkMode, ref enabled, Marshal.SizeOf<int>());
        _ = DwmSetWindowAttribute(hwnd, DwmWindowAttributeUseImmersiveDarkModeBefore20, ref enabled, Marshal.SizeOf<int>());
    }

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);
}
