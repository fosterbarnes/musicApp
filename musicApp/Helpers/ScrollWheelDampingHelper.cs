using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace musicApp.Helpers;

public static class ScrollWheelDampingHelper
{
    private const double DeltaScale = 0.4;

    public static void Attach(UIElement target)
    {
        if (target == null)
            return;

        target.PreviewMouseWheel -= OnPreviewMouseWheel;
        target.PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled)
            return;

        var scrollViewer = FindScrollViewer(sender as DependencyObject);
        if (scrollViewer == null)
            return;

        var delta = e.Delta * DeltaScale;
        var target = scrollViewer.VerticalOffset - delta;
        target = Math.Min(Math.Max(0, target), scrollViewer.ScrollableHeight);
        scrollViewer.ScrollToVerticalOffset(target);
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject? start)
    {
        for (var d = start; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is ScrollViewer sv)
                return sv;
        }

        if (start is FrameworkElement fe)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(fe); i++)
            {
                var child = VisualTreeHelper.GetChild(fe, i);
                var nested = FindScrollViewer(child);
                if (nested != null)
                    return nested;
            }
        }

        return null;
    }
}
