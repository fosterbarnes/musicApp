using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace musicApp.Helpers;

public static class SectionSegmentUi
{
    public static void ApplySegmentStates(FrameworkElement resourceScope, IReadOnlyList<Button> segmentsInOrder, int activeIndex)
    {
        if (activeIndex < 0)
            activeIndex = 0;

        var muted = resourceScope.TryFindResource("BorderMuted-brush") as Brush;
        if (muted == null || resourceScope.TryFindResource("SectionSegmentSelectedBorder-brush") as Brush == null)
            return;

        double rad = 0;
        if (resourceScope.TryFindResource("Sm-cr") is CornerRadius smCr)
            rad = smCr.TopLeft;

        for (var i = 0; i < segmentsInOrder.Count; i++)
        {
            var btn = segmentsInOrder[i];
            var isActive = i == activeIndex;
            var isFirst = i == 0;
            var isLast = i == segmentsInOrder.Count - 1;

            var shell = isFirst
                ? new CornerRadius(rad, 0, 0, rad)
                : isLast
                    ? new CornerRadius(0, rad, rad, 0)
                    : new CornerRadius(0);

            SectionSegmentChrome.SetChromeShellCornerRadius(btn, shell);

            var styleKey = isActive ? "SectionSegmentActiveStyle" : "SectionSegmentInactiveStyle";
            if (resourceScope.TryFindResource(styleKey) is Style style)
                btn.Style = style;

            if (!isActive)
            {
                Brush left, right;
                if (isFirst)
                {
                    left = muted;
                    right = activeIndex == 1 ? Brushes.Transparent : muted;
                }
                else if (isLast)
                {
                    left = Brushes.Transparent;
                    right = muted;
                }
                else
                {
                    left = i == 1
                        ? (activeIndex == 0 ? Brushes.Transparent : muted)
                        : Brushes.Transparent;
                    right = activeIndex == i + 1 ? Brushes.Transparent : muted;
                }

                SectionSegmentChrome.SetChromeTopBrush(btn, muted);
                SectionSegmentChrome.SetChromeBottomBrush(btn, muted);
                SectionSegmentChrome.SetChromeLeftBrush(btn, left);
                SectionSegmentChrome.SetChromeRightBrush(btn, right);
            }

            btn.SizeChanged -= SegmentButtonClipOnSizeChanged;
            btn.SizeChanged += SegmentButtonClipOnSizeChanged;
            ApplySegmentClip(btn);
        }
    }

    private static void SegmentButtonClipOnSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplySegmentClip((Button)sender);

    private static void ApplySegmentClip(Button btn)
    {
        var cr = SectionSegmentChrome.GetChromeShellCornerRadius(btn);
        var clip = SectionSegmentRoundRectClip.Create(btn.ActualWidth, btn.ActualHeight, cr);
        btn.Clip = clip;
    }
}
