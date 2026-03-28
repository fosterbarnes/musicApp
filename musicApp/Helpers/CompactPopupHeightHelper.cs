using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using musicApp.Constants;

namespace musicApp.Helpers;

public static class CompactPopupHeightHelper
{
    public static void ScheduleAdjust(
        Dispatcher dispatcher,
        int generationAtSchedule,
        Func<int> getCurrentGeneration,
        Action adjustAction)
    {
        _ = dispatcher.BeginInvoke(new Action(() =>
        {
            if (generationAtSchedule != getCurrentGeneration())
                return;
            adjustAction();
        }), DispatcherPriority.ContextIdle);
    }

    public static void AdjustBorderHeightToContent(
        Border popupBorder,
        ScrollViewer scrollViewer,
        Panel contentPanel,
        UIElement layoutRoot)
    {
        popupBorder.Height = UILayoutConstants.CompactPopupDefaultHeight;
        contentPanel.InvalidateMeasure();
        scrollViewer.InvalidateMeasure();
        layoutRoot.UpdateLayout();
        scrollViewer.UpdateLayout();

        double viewportHeight = scrollViewer.ViewportHeight;
        double actualBorderHeight = popupBorder.ActualHeight;
        if (viewportHeight <= 0 || actualBorderHeight <= 0)
        {
            popupBorder.Height = UILayoutConstants.CompactPopupDefaultHeight;
            return;
        }

        double overhead = actualBorderHeight - viewportHeight;

        double measureWidth = scrollViewer.ViewportWidth;
        if (measureWidth <= 0)
            measureWidth = Math.Max(
                popupBorder.ActualWidth - UILayoutConstants.CompactPopupHorizontalContentPadding,
                popupBorder.MinWidth - UILayoutConstants.CompactPopupHorizontalContentPadding);

        contentPanel.Measure(new Size(measureWidth, double.PositiveInfinity));
        double contentHeight = Math.Max(contentPanel.DesiredSize.Height, scrollViewer.ExtentHeight);

        double desiredTotalHeight = contentHeight + overhead;

        popupBorder.Height = desiredTotalHeight >= UILayoutConstants.CompactPopupDefaultHeight
            ? UILayoutConstants.CompactPopupDefaultHeight
            : Math.Max(UILayoutConstants.CompactPopupMinHeight, desiredTotalHeight);
    }

    public static double GetAvailableMaxHeight(DependencyObject popupChild)
    {
        var hostWindow = Window.GetWindow(popupChild);
        if (hostWindow == null || hostWindow.ActualHeight <= 0)
            return UILayoutConstants.CompactPopupMaxHeight;

        var windowLimitedMax = hostWindow.ActualHeight - UILayoutConstants.CompactPopupWindowVerticalOverhead;
        return Math.Clamp(
            windowLimitedMax,
            UILayoutConstants.CompactPopupMinHeight,
            UILayoutConstants.CompactPopupMaxHeight);
    }
}
