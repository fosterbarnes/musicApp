using System.Windows;
using System.Windows.Media;

namespace musicApp.Helpers;

public static class SectionSegmentChrome
{
    public static readonly DependencyProperty ChromeShellCornerRadiusProperty =
        DependencyProperty.RegisterAttached(
            "ChromeShellCornerRadius",
            typeof(CornerRadius),
            typeof(SectionSegmentChrome),
            new FrameworkPropertyMetadata(default(CornerRadius), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ChromeTopBrushProperty =
        DependencyProperty.RegisterAttached(
            "ChromeTopBrush",
            typeof(Brush),
            typeof(SectionSegmentChrome),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ChromeBottomBrushProperty =
        DependencyProperty.RegisterAttached(
            "ChromeBottomBrush",
            typeof(Brush),
            typeof(SectionSegmentChrome),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ChromeLeftBrushProperty =
        DependencyProperty.RegisterAttached(
            "ChromeLeftBrush",
            typeof(Brush),
            typeof(SectionSegmentChrome),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ChromeRightBrushProperty =
        DependencyProperty.RegisterAttached(
            "ChromeRightBrush",
            typeof(Brush),
            typeof(SectionSegmentChrome),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static CornerRadius GetChromeShellCornerRadius(DependencyObject d) =>
        (CornerRadius)d.GetValue(ChromeShellCornerRadiusProperty);

    public static void SetChromeShellCornerRadius(DependencyObject d, CornerRadius value) =>
        d.SetValue(ChromeShellCornerRadiusProperty, value);

    public static Brush? GetChromeTopBrush(DependencyObject d) =>
        (Brush?)d.GetValue(ChromeTopBrushProperty);

    public static void SetChromeTopBrush(DependencyObject d, Brush? value) =>
        d.SetValue(ChromeTopBrushProperty, value);

    public static Brush? GetChromeBottomBrush(DependencyObject d) =>
        (Brush?)d.GetValue(ChromeBottomBrushProperty);

    public static void SetChromeBottomBrush(DependencyObject d, Brush? value) =>
        d.SetValue(ChromeBottomBrushProperty, value);

    public static Brush? GetChromeLeftBrush(DependencyObject d) =>
        (Brush?)d.GetValue(ChromeLeftBrushProperty);

    public static void SetChromeLeftBrush(DependencyObject d, Brush? value) =>
        d.SetValue(ChromeLeftBrushProperty, value);

    public static Brush? GetChromeRightBrush(DependencyObject d) =>
        (Brush?)d.GetValue(ChromeRightBrushProperty);

    public static void SetChromeRightBrush(DependencyObject d, Brush? value) =>
        d.SetValue(ChromeRightBrushProperty, value);
}
