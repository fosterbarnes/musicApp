using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using musicApp.Helpers;

namespace musicApp.Views
{
    public partial class AlbumsView
    {
        private List<int> GetVisibleIndices()
        {
            var result = new List<int>();
            if (AlbumScrollViewer == null || _albumItems.Count == 0)
                return result;

            if (AlbumScrollViewer.ViewportHeight <= 0)
                return result;

            AlbumGridLayoutMath.GetStrides(
                CurrentTileSize, TileMargin.Right, TileMargin.Bottom, TileScaleRatio,
                out double tileStrideX, out double tileStrideY);
            int perRow = AlbumGridLayoutMath.PerRowFromViewport(AlbumScrollViewer.ViewportWidth, tileStrideX);

            int startRow = Math.Max(0, (int)Math.Floor(AlbumScrollViewer.VerticalOffset / tileStrideY) - 2);
            int endRow = (int)Math.Ceiling((AlbumScrollViewer.VerticalOffset + AlbumScrollViewer.ViewportHeight) / tileStrideY) + 2;
            int startIndex = Math.Max(0, startRow * perRow);
            int endIndex = Math.Min(_albumItems.Count - 1, ((endRow + 1) * perRow) - 1);

            var gen = AlbumGrid.ItemContainerGenerator;
            for (int i = startIndex; i <= endIndex; i++)
            {
                if (_albumItems[i] is not AlbumGridItem) continue;

                var container = gen.ContainerFromIndex(i) as FrameworkElement;
                if (container == null)
                    continue;

                GeneralTransform transform;
                try { transform = container.TransformToAncestor(AlbumScrollViewer); }
                catch { continue; }

                var topLeft = transform.Transform(new Point(0, 0));
                double itemTop = topLeft.Y;
                double itemBottom = itemTop + container.ActualHeight;

                if (itemBottom >= 0 && itemTop <= AlbumScrollViewer.ViewportHeight)
                    result.Add(i);
            }

            return result;
        }

        private void AlbumsView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SyncSectionHeaderWidth();
            TryCaptureResizeAnchor();
            _resizeAnchorDebounce.Stop();
            _resizeAnchorDebounce.Start();

            ScheduleArtLoad();
            if (_currentFlyout == null || _selectedAlbum == null)
                return;

            FlyoutPanelWidth = GetWrapPanelWidth();
            _flyoutResizeDebounce.Stop();
            _flyoutResizeDebounce.Start();
        }

        private bool TryCaptureResizeAnchor()
        {
            if (AlbumScrollViewer == null)
                return false;

            if (_selectedAlbum != null)
            {
                if (!TryGetAlbumItemTopInViewport(_selectedAlbum, out var itemTop))
                    itemTop = 12;

                _pendingResizeAnchor = new ResizeAnchorState(
                    _selectedAlbum.AlbumTitle,
                    _selectedAlbum.Artist,
                    itemTop,
                    ResizeAnchorKind.SelectedAlbum);
                return true;
            }

            if (!TryGetFirstVisibleAlbumItem(out var visibleAlbum, out var visibleTop) || visibleAlbum == null)
                return false;

            _pendingResizeAnchor = new ResizeAnchorState(
                visibleAlbum.AlbumTitle,
                visibleAlbum.Artist,
                visibleTop,
                ResizeAnchorKind.FirstVisibleAlbum);
            return true;
        }

        private bool TryGetFirstVisibleAlbumItem(out AlbumGridItem? album, out double itemTop)
        {
            album = null;
            itemTop = 0;

            if (AlbumScrollViewer == null || _albumItems.Count == 0)
                return false;

            var visibleIndices = GetVisibleIndices();
            if (visibleIndices.Count == 0)
                return false;

            double bestTop = double.MaxValue;
            foreach (var index in visibleIndices)
            {
                if (index < 0 || index >= _albumItems.Count)
                    continue;

                if (_albumItems[index] is not AlbumGridItem candidate)
                    continue;

                if (!TryGetAlbumItemTopInViewport(candidate, out var top))
                    continue;

                if (top < bestTop)
                {
                    bestTop = top;
                    album = candidate;
                    itemTop = top;
                }
            }

            return album != null;
        }

        private bool TryGetAlbumItemTopInViewport(AlbumGridItem album, out double itemTop)
        {
            itemTop = 0;
            if (AlbumScrollViewer == null)
                return false;

            var container = AlbumGrid.ItemContainerGenerator.ContainerFromItem(album) as FrameworkElement;
            if (container == null)
                return false;

            try
            {
                var transform = container.TransformToAncestor(AlbumScrollViewer);
                itemTop = transform.Transform(new Point(0, 0)).Y;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void RestoreResizeAnchorIfPending()
        {
            if (!_pendingResizeAnchor.HasValue || AlbumScrollViewer == null || _albumItems.Count == 0)
                return;

            var pending = _pendingResizeAnchor.Value;
            var anchor = FindAlbumItem(pending.AlbumTitle, pending.Artist);
            if (anchor == null)
            {
                _pendingResizeAnchor = null;
                return;
            }

            AlbumGrid.UpdateLayout();
            AlbumScrollViewer.UpdateLayout();

            if (!TryGetAlbumItemTopInViewport(anchor, out var currentTop))
            {
                _pendingResizeAnchor = null;
                return;
            }

            double desiredTop = pending.OffsetFromViewportTop;
            if (pending.Kind == ResizeAnchorKind.SelectedAlbum)
                desiredTop = Math.Min(Math.Max(0, desiredTop), Math.Max(0, AlbumScrollViewer.ViewportHeight - CurrentTileSize));
            else
                desiredTop = Math.Max(0, desiredTop);

            double targetOffset = AlbumScrollViewer.VerticalOffset + (currentTop - desiredTop);
            targetOffset = Math.Min(Math.Max(0, targetOffset), AlbumScrollViewer.ScrollableHeight);
            AlbumScrollViewer.ScrollToVerticalOffset(targetOffset);
            _pendingResizeAnchor = null;
        }
    }
}
