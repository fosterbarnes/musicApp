using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace musicApp.Helpers;

public static class SectionSegmentChrome
{
    public static readonly DependencyProperty ChromeShellCornerRadiusProperty =
        DependencyProperty.RegisterAttached(
            "ChromeShellCornerRadius",
            typeof(CornerRadius),
            typeof(SectionSegmentChrome),
            new FrameworkPropertyMetadata(default(CornerRadius), FrameworkPropertyMetadataOptions.AffectsRender));

    public static CornerRadius GetChromeShellCornerRadius(DependencyObject d) =>
        (CornerRadius)d.GetValue(ChromeShellCornerRadiusProperty);

    public static void SetChromeShellCornerRadius(DependencyObject d, CornerRadius value) =>
        d.SetValue(ChromeShellCornerRadiusProperty, value);
}

public static class SectionSegmentUi
{
    public static void ApplySegmentStates(FrameworkElement resourceScope, IReadOnlyList<Button> segmentsInOrder, int activeIndex)
    {
        if (activeIndex < 0)
            activeIndex = 0;

        double rad = 0;
        if (resourceScope.TryFindResource("Sm-cr") is CornerRadius smCr)
            rad = smCr.TopLeft;

        var last = segmentsInOrder.Count - 1;
        for (var i = 0; i <= last; i++)
        {
            var btn = segmentsInOrder[i];
            var isActive = i == activeIndex;

            SectionSegmentChrome.SetChromeShellCornerRadius(btn, new CornerRadius(
                i == 0 ? rad : 0, i == last ? rad : 0, i == last ? rad : 0, i == 0 ? rad : 0));

            var styleKey = isActive ? "SectionSegmentActiveStyle" : "SectionSegmentInactiveStyle";
            if (resourceScope.TryFindResource(styleKey) is Style style)
                btn.Style = style;

            // Segments overlap by 1px; the active one draws on top so its outline covers neighbors'.
            Panel.SetZIndex(btn, isActive ? 1 : 0);
        }
    }
}
