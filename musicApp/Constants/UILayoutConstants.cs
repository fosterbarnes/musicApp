using System;

namespace MusicApp.Constants;

public static class UILayoutConstants
{
    // Main window defaults
    public const int DefaultWindowLeft = 100;
    public const int DefaultWindowTop = 100;
    public const int DefaultWindowWidth = 1200;
    public const int DefaultWindowHeight = 700;

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

    // Search popup layout
    public const double SearchPopupHorizontalContentPadding = 24;
    public const double SearchPopupWindowVerticalOverhead = 140;

    // Track list layout
    public const double TrackListMinimumColumnWidth = 50.0;

    // Albums view layout
    public const double AlbumTrackMetaRowHeight = 40.0;
    public const int AlbumRebuildBatchSize = 64;
    public const int InitialAlbumArtLoadCount = 100;
    public const int AlbumVisibleRangeOverscan = 30;
    public const int AlbumArtMinimumTargetSize = 80;
    public const double AlbumWrapFallbackWidth = 400.0;
    public const double AlbumWrapHorizontalPadding = 48.0;
}
