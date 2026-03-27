using System;
using musicApp.Constants;

namespace musicApp.Helpers;

/// <summary>Maps scan-style resource advice to album grid UI append batch sizes.</summary>
public static class AlbumRebuildUiAdvisor
{
    public static int ItemsPerDispatcherBatch(int smoothedDopFromScanAdvisor)
    {
        int raw = smoothedDopFromScanAdvisor * UILayoutConstants.AlbumRebuildBatchDopScale;
        return Math.Clamp(raw, UILayoutConstants.AlbumRebuildBatchMin, UILayoutConstants.AlbumRebuildBatchMax);
    }

    public static int PrefixPhaseItemsPerBatch(int baseBatch)
    {
        int boosted = baseBatch * UILayoutConstants.AlbumRebuildPrefixBatchMultiplier;
        return Math.Min(boosted, UILayoutConstants.AlbumRebuildPrefixMaxBatch);
    }
}
