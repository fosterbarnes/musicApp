using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
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

internal static class SectionSegmentRoundRectClip
{
    public static Geometry? Create(double width, double height, CornerRadius r)
    {
        if (width <= 0 || height <= 0)
            return null;

        static double ClampRad(double rad, double w, double h)
        {
            if (rad <= 0)
                return 0;
            var max = Math.Min(w, h) / 2;
            return Math.Min(rad, max);
        }

        var tl = ClampRad(r.TopLeft, width, height);
        var tr = ClampRad(r.TopRight, width, height);
        var br = ClampRad(r.BottomRight, width, height);
        var bl = ClampRad(r.BottomLeft, width, height);

        if (tl == 0 && tr == 0 && br == 0 && bl == 0)
            return null;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(tl, 0), true, true);
            ctx.LineTo(new Point(width - tr, 0), true, false);
            if (tr > 0)
                ctx.ArcTo(new Point(width, tr), new Size(tr, tr), 0, false, SweepDirection.Clockwise, true, false);
            else
                ctx.LineTo(new Point(width, 0), true, false);

            ctx.LineTo(new Point(width, height - br), true, false);
            if (br > 0)
                ctx.ArcTo(new Point(width - br, height), new Size(br, br), 0, false, SweepDirection.Clockwise, true, false);
            else
                ctx.LineTo(new Point(width, height), true, false);

            ctx.LineTo(new Point(bl, height), true, false);
            if (bl > 0)
                ctx.ArcTo(new Point(0, height - bl), new Size(bl, bl), 0, false, SweepDirection.Clockwise, true, false);
            else
                ctx.LineTo(new Point(0, height), true, false);

            ctx.LineTo(new Point(0, tl), true, false);
            if (tl > 0)
                ctx.ArcTo(new Point(tl, 0), new Size(tl, tl), 0, false, SweepDirection.Clockwise, true, false);
            else
                ctx.LineTo(new Point(0, 0), true, false);
        }
        geo.Freeze();
        return geo;
    }
}

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
