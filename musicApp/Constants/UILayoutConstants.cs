using System;

namespace musicApp.Constants;

public static class UILayoutConstants
{
    // Main window defaults
    public const int DefaultWindowLeft = 100;
    public const int DefaultWindowTop = 100;
    public const int DefaultWindowWidth = 1200;
    public const int DefaultWindowHeight = 700;

    /// <summary>Sidebar column min width; also the default width when no saved width applies.</summary>
    public const double SidebarMinWidth = 180;

    // Playback and state debounce
    public const int ManualNavigationResetDelayMs = 100;
    public static readonly TimeSpan SidebarWidthSaveDelay = TimeSpan.FromSeconds(0.5);
    public static readonly TimeSpan ColumnWidthSaveDelay = TimeSpan.FromSeconds(0.5);

    // Playback navigation thresholds
    public const double PreviousTrackRestartThresholdSeconds = 3.0;
    public const double PreviousTrackEdgeThresholdSeconds = 2.0;

    // Shared image sizing
    public const int TitleBarAlbumArtRenderSize = 120;
    public const int InfoMetadataAlbumArtSize = 144;
    /// <summary>Logical width/height of the Artwork tab inner square; Viewbox scales it to fit the window.</summary>
    public const double InfoMetadataArtworkViewboxLogicalExtent = 512;

    // Compact popups (search, queue popout): shared geometry
    public const double CompactPopupMinHeight = 160;
    public const double CompactPopupDefaultHeight = 575;
    public const double CompactPopupMaxHeight = 850;
    public const double CompactPopupHorizontalContentPadding = 24;
    public const double CompactPopupWindowVerticalOverhead = 140;

    /// <summary>Alias retained for older call sites; same as <see cref="CompactPopupHorizontalContentPadding"/>.</summary>
    public const double SearchPopupHorizontalContentPadding = CompactPopupHorizontalContentPadding;

    /// <summary>Alias retained for older call sites; same as <see cref="CompactPopupWindowVerticalOverhead"/>.</summary>
    public const double SearchPopupWindowVerticalOverhead = CompactPopupWindowVerticalOverhead;

    /// <summary>Negative = shift search results popup left relative to default bottom placement.</summary>
    public const double SearchPopupHorizontalOffsetNudge = -24;

    /// <summary><see cref="System.Windows.Controls.Primitives.Popup.VerticalOffset"/> for title-bar queue popout (<c>Placement=Bottom</c>).</summary>
    public const double CompactPopupTitleBarVerticalOffset = 20;

    /// <summary>Search popout sits 2px higher than <see cref="CompactPopupTitleBarVerticalOffset"/>.</summary>
    public const double SearchPopupTitleBarVerticalOffset = CompactPopupTitleBarVerticalOffset - 2;

    // Track list layout
    public const double TrackListMinimumColumnWidth = 50.0;
    public const double TrackListQueueOrderColumnMinWidth = 26.0;

    // Albums view layout
    public const double AlbumTrackMetaRowHeight = 40.0;
    public const int AlbumRebuildBatchSize = 64;

    /// <summary>UI append batch = smoothed scan DOP × scale, clamped to min/max.</summary>
    public const int AlbumRebuildBatchMin = 32;
    public const int AlbumRebuildBatchMax = 192;
    public const int AlbumRebuildBatchDopScale = 12;

    /// <summary>While filling the prefix (first screen or up to pending album), multiply batch size.</summary>
    public const int AlbumRebuildPrefixBatchMultiplier = 2;
    public const int AlbumRebuildPrefixMaxBatch = 384;
    /// <summary>Extra wrap-panel rows beyond viewport when racing to a pending album index.</summary>
    public const int AlbumRebuildPrefixOverscanRows = 3;

    /// <summary>Re-sample RAM/CPU after this many UI batches (same idea as library scan).</summary>
    public const int AlbumRebuildMetricsResampleEveryNBatches = 2;

    public const int AlbumArtLoadMaxParallelism = 16;

    /// <summary>Full-grid prefetch: max parallel decodes (lower than viewport).</summary>
    public const int AlbumArtPrefetchMaxParallelism = 6;

    /// <summary>Album indices per prefetch batch (top to bottom).</summary>
    public const int AlbumArtPrefetchIndexChunk = 48;

    public const int AlbumArtPrefetchMetricsResampleEveryNBatches = 3;

    /// <summary>When the scroll viewport has no height yet, assume this many DIPs tall for bootstrap art index range.</summary>
    public const double AlbumArtBootstrapViewportHeightFallback = 640.0;

    /// <summary>Minimum wrap rows to load when estimating visible range without live container hits.</summary>
    public const int AlbumArtViewportBootstrapMinRows = 4;

    public const int AlbumVisibleRangeOverscan = 30;
    public const int AlbumArtMinimumTargetSize = 80;
    public const double AlbumWrapFallbackWidth = 400.0;
    public const double AlbumWrapHorizontalPadding = 48.0;
}
