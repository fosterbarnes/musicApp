using System;
using System.Windows;
using System.Windows.Threading;

namespace musicApp.Helpers;

public static class WindowFocusHelper
{
    public static void ScheduleActivate(Window? window)
    {
        if (window == null)
            return;
        var d = window.Dispatcher;
        if (d.HasShutdownStarted)
            return;

        d.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            if (!window.IsVisible)
                return;
            window.Activate();
            _ = window.Focus();
        }));
    }

    public static void ScheduleActivateOwner(Window child)
    {
        ScheduleActivate(child.Owner as Window);
    }
}
