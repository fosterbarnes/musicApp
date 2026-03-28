using System;
using musicApp.Constants;

namespace musicApp.Helpers;

internal static class AlbumGridLayoutMath
{
    public static void GetStrides(double tileSize, double marginRight, double marginBottom, double tileScaleRatio,
        out double tileStrideX, out double tileStrideY)
    {
        tileStrideX = Math.Max(1, tileSize + Math.Max(0, marginRight));
        tileStrideY = Math.Max(1, tileSize + Math.Max(0, marginBottom) + UILayoutConstants.AlbumTrackMetaRowHeight * tileScaleRatio);
    }

    public static int PerRowFromViewport(double viewportWidth, double tileStrideX) =>
        Math.Max(1, (int)Math.Floor(Math.Max(1, viewportWidth) / tileStrideX));
}
