using System;
using System.Windows;
using System.Windows.Media;

namespace musicApp.Helpers;

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
