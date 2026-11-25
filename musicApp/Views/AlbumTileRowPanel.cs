using System.Windows.Controls;

namespace musicApp.Views;

/// <summary>
/// Shows one <see cref="AlbumTileRow"/> left to right. When the grid recycles a row container for another row,
/// the tile presenters are kept and only handed new albums, so their templates are not rebuilt (an
/// ItemsControl here re-created every tile, which made scrollbar jumps and mode switches slow).
/// </summary>
public sealed class AlbumTileRowPanel : StackPanel
{
    public AlbumTileRowPanel()
    {
        Orientation = Orientation.Horizontal;
        DataContextChanged += (_, _) => ShowRow(DataContext as AlbumTileRow);
    }

    private void ShowRow(AlbumTileRow? row)
    {
        int count = row?.Albums.Count ?? 0;
        if (Children.Count > count)
            Children.RemoveRange(count, Children.Count - count);
        while (Children.Count < count)
            Children.Add(new ContentPresenter());
        for (int i = 0; i < count && row != null; i++)
            ((ContentPresenter)Children[i]).Content = row.Albums[i];
    }
}
