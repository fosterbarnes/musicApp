using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using musicApp.Constants;
using musicApp.Helpers;

namespace musicApp.Views
{
    // Row layout: _entries (tiles and headers) are chunked into AlbumTileRow rows of tiles-per-row albums.
    // Rows are re-chunked only when tiles-per-row or the entries change; everything else is row lookups.
    public partial class AlbumsView
    {
        private VirtualizingStackPanel? ItemsHost => FindVisualChild<VirtualizingStackPanel>(_itemsPresenter);

        /// <summary>Width rows can use: the scroll viewer's content area (minus padding and scrollbar).</summary>
        private double AvailableRowWidth() =>
            _rowSpace.ActualWidth > 0
                ? _rowSpace.ActualWidth
                : Math.Max(UILayoutConstants.AlbumWrapFallbackWidth, ActualWidth - UILayoutConstants.AlbumWrapHorizontalPadding);

        private double TileStrideX()
        {
            AlbumGridLayoutMath.GetStrides(CurrentTileSize, TileMargin.Right, TileMargin.Bottom, TileScaleRatio, out var sx, out _);
            return sx;
        }

        // Recently Added tiles wrap inside the header width (call SyncSectionHeaderWidth first).
        private int ComputeTilesPerRow() => AlbumGridLayoutMath.PerRowFromViewport(
            BrowseMode == AlbumsBrowseMode.RecentlyAdded ? SectionHeaderWidth : AvailableRowWidth(), TileStrideX());

        /// <summary>Recently Added rows span the header width (tiles start at the left); Albums rows are centered.</summary>
        private void UpdateRowWidth()
        {
            RowWidth = BrowseMode == AlbumsBrowseMode.RecentlyAdded ? SectionHeaderWidth : _tilesPerRow * TileStrideX();
            FlyoutPanelWidth = RowWidth;
        }

        /// <summary>Rebuilds the row list from <see cref="_entries"/>, keeping the open flyout under its album's row.</summary>
        private void RebuildRows()
        {
            SyncSectionHeaderWidth();
            _tilesPerRow = ComputeTilesPerRow();
            UpdateRowWidth();

            var rows = new List<object>(_entries.Count / _tilesPerRow + 8);
            List<AlbumGridItem>? row = null;
            foreach (var entry in _entries)
            {
                if (entry is AlbumGridItem album)
                {
                    row ??= new List<AlbumGridItem>(_tilesPerRow);
                    row.Add(album);
                    if (row.Count < _tilesPerRow)
                        continue;
                }
                else if (row == null)
                {
                    rows.Add(entry);
                    continue;
                }

                rows.Add(new AlbumTileRow(row));
                row = null;
                if (entry is not AlbumGridItem)
                    rows.Add(entry); // a header ends the row before it
            }
            if (row != null)
                rows.Add(new AlbumTileRow(row));

            if (_currentFlyout != null && _selectedAlbum != null)
            {
                int at = rows.FindIndex(r => r is AlbumTileRow t && t.Albums.Contains(_selectedAlbum));
                if (at >= 0)
                    rows.Insert(at + 1, _currentFlyout);
                else
                {
                    _currentFlyout = null;
                    _selectedAlbum = null;
                    ClearFlyoutTrackSelection();
                }
            }

            // Update in place rather than swapping in a new collection: a Reset makes the panel drop every row
            // container (and its tiles); replacing items lets it rebind the containers it already has.
            int keep = Math.Min(_rows.Count, rows.Count);
            for (int i = 0; i < keep; i++)
            {
                if (!ReferenceEquals(_rows[i], rows[i]))
                    _rows[i] = rows[i];
            }
            while (_rows.Count > rows.Count)
                _rows.RemoveAt(_rows.Count - 1);
            for (int i = keep; i < rows.Count; i++)
                _rows.Add(rows[i]);

            if (_currentFlyout != null && _selectedAlbum != null)
                UpdateFlyoutArrowOffset(_selectedAlbum);
        }

        /// <summary>
        /// The space for rows changed (window resize, scrollbar shown or hidden): re-chunk if tiles-per-row
        /// changed, keeping the open album (when on screen) or the first visible album at the same height.
        /// </summary>
        private void RechunkRowsKeepingPlace()
        {
            SyncSectionHeaderWidth();
            if (_isDraggingSlider || _entries.Count == 0)
                return;
            if (ComputeTilesPerRow() == _tilesPerRow)
            {
                UpdateRowWidth();
                return;
            }

            var anchor = CaptureViewAnchor();
            RebuildRows();
            if (anchor is { } a)
                ScrollRowTo(RowIndexOf(a.Album), a.Top);
        }

        private (AlbumGridItem Album, double Top)? CaptureViewAnchor()
        {
            // The open album anchors only while it is on screen; scrolled far away, anchoring it made a
            // resize jump back to it.
            if (_selectedAlbum != null && TryGetRowTop(RowIndexOf(_selectedAlbum), out var selectedTop) &&
                selectedTop > -CurrentTileSize && selectedTop < _albumScrollViewer.ViewportHeight)
                return (_selectedAlbum, selectedTop);

            foreach (var (row, top, bottom) in RealizedRows())
            {
                if (bottom > 0 && row is AlbumTileRow tiles)
                    return (tiles.Albums[0], top);
            }
            return null;
        }

        /// <summary>Realized row containers in order, with their top and bottom relative to the viewport.</summary>
        private IEnumerable<(object Row, double Top, double Bottom)> RealizedRows()
        {
            if (ItemsHost is not { } host)
                yield break;
            var generator = AlbumGrid.ItemContainerGenerator;
            foreach (UIElement child in host.Children)
            {
                // Recycled containers waiting for reuse have no index.
                if (child is not FrameworkElement fe || fe.ActualHeight <= 0 || generator.IndexFromContainer(fe) < 0)
                    continue;
                double top;
                try { top = fe.TransformToAncestor(_albumScrollViewer).Transform(new Point(0, 0)).Y; }
                catch (InvalidOperationException) { continue; }
                yield return (fe.DataContext, top, top + fe.ActualHeight);
            }
        }

        private int RowIndexOf(AlbumGridItem album)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] is AlbumTileRow row && row.Albums.Contains(album))
                    return i;
            }
            return -1;
        }

        /// <summary>Index in <see cref="_albums"/> of the first album in the first visible tile row (0 if none).</summary>
        private int FirstVisibleAlbumIndex()
        {
            foreach (var (row, _, bottom) in RealizedRows())
            {
                if (bottom > 0 && row is AlbumTileRow tiles)
                    return Math.Max(0, _albums.IndexOf(tiles.Albums[0]));
            }
            return 0;
        }

        private int AlbumsPerScreen()
        {
            AlbumGridLayoutMath.GetStrides(CurrentTileSize, TileMargin.Right, TileMargin.Bottom, TileScaleRatio, out _, out var sy);
            double vh = _albumScrollViewer.ViewportHeight > 0 ? _albumScrollViewer.ViewportHeight : UILayoutConstants.AlbumArtBootstrapViewportHeightFallback;
            return Math.Max(1, _tilesPerRow) * ((int)Math.Ceiling(vh / sy) + 1);
        }

        private bool TryGetRowTop(int rowIndex, out double top)
        {
            top = 0;
            if (rowIndex < 0 || AlbumGrid.ItemContainerGenerator.ContainerFromIndex(rowIndex) is not FrameworkElement container)
                return false;
            try { top = container.TransformToAncestor(_albumScrollViewer).Transform(new Point(0, 0)).Y; }
            catch (InvalidOperationException) { return false; }
            return true;
        }

        /// <summary>Scrolls so row <paramref name="rowIndex"/> starts <paramref name="desiredTop"/> below the viewport top.</summary>
        private void ScrollRowTo(int rowIndex, double desiredTop)
        {
            if (rowIndex < 0 || ItemsHost is not { } host)
                return;
            AlbumGrid.UpdateLayout();
            if (!TryGetRowTop(rowIndex, out var top))
            {
                host.BringIndexIntoViewPublic(rowIndex); // realizes the row so it can be measured
                AlbumGrid.UpdateLayout();
                if (!TryGetRowTop(rowIndex, out top))
                    return;
            }
            _albumScrollViewer.ScrollToVerticalOffset(Math.Max(0, _albumScrollViewer.VerticalOffset + top - desiredTop));
        }

        private void AlbumsView_SizeChanged(object sender, SizeChangedEventArgs e) => SyncSectionHeaderWidth();
    }
}
