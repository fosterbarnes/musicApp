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

    // State debounce
    public static readonly TimeSpan SidebarWidthSaveDelay = TimeSpan.FromSeconds(0.5);
    public static readonly TimeSpan ColumnWidthSaveDelay = TimeSpan.FromSeconds(0.5);

    // Playback navigation thresholds
    public const double PreviousTrackEdgeThresholdSeconds = 2.0;

    // Shared image sizing
    /// <summary>Logical width/height (dip) of the title bar album slot (<see cref="TitleBarPlus.TitleBar"/>).</summary>
    public const double TitleBarAlbumArtLogicalSizeDip = 50;
    public const int InfoMetadataAlbumArtSize = 144;
    /// <summary>Logical width/height of the Artwork tab inner square; Viewbox scales it to fit the window.</summary>
    public const double InfoMetadataArtworkViewboxLogicalExtent = 512;

    // Compact popups (search, queue popout): shared geometry
    public const double CompactPopupMinHeight = 160;
    public const double CompactPopupDefaultHeight = 575;
    public const double CompactPopupMaxHeight = 850;
    public const double CompactPopupHorizontalContentPadding = 24;
    public const double CompactPopupWindowVerticalOverhead = 140;

    /// <summary>Max songs in compact queue UIs (miniplayer + title-bar queue popup). Caps UI cost for huge library sessions.</summary>
    public const int CompactQueueListMaxItems = 64;

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

    /// <summary>When the grid has no viewport height yet, assume this many DIPs tall.</summary>
    public const double AlbumArtBootstrapViewportHeightFallback = 640.0;

    public const int AlbumArtMinimumTargetSize = 80;
    public const double AlbumWrapFallbackWidth = 400.0;
    public const double AlbumWrapHorizontalPadding = 48.0;
}
