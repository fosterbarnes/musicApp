using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using musicApp.Views;

namespace musicApp.Helpers;

public static class ListRowReorderHelper
{
    public static int InsertBeforeToMoveIndex(int fromIndex, int insertBeforeIndex) =>
        fromIndex < insertBeforeIndex ? insertBeforeIndex - 1 : insertBeforeIndex;

    public static bool IsChromeButton(DependencyObject? node)
    {
        while (node != null)
        {
            if (node is Button)
                return true;
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    public static int TryGetRowIndexFromSource(
        DependencyObject? source,
        ObservableCollection<SongRowViewModel>? rows)
    {
        if (source == null || rows == null)
            return -1;

        while (source != null)
        {
            if (source is FrameworkElement fe && fe.DataContext is SongRowViewModel row)
                return rows.IndexOf(row);
            source = VisualTreeHelper.GetParent(source);
        }
        return -1;
    }

    public static FrameworkElement? FindRowElementForViewModel(DependencyObject root, SongRowViewModel target)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe &&
                fe.ContextMenu != null &&
                ReferenceEquals(fe.DataContext, target))
                return fe;

            var nested = FindRowElementForViewModel(child, target);
            if (nested != null)
                return nested;
        }
        return null;
    }

    public static FrameworkElement? FindRowElementAtIndex(
        ItemsControl itemsControl,
        ObservableCollection<SongRowViewModel> rows,
        int index)
    {
        if (index < 0 || index >= rows.Count)
            return null;
        return FindRowElementForViewModel(itemsControl, rows[index]);
    }

    public static void GetDropGeometry(
        UIElement hitRoot,
        ItemsControl itemsControl,
        ObservableCollection<SongRowViewModel> rows,
        Point listPos,
        int minIndex,
        out int toIndex,
        out double lineY)
    {
        toIndex = -1;
        lineY = 0;

        int count = rows.Count;
        if (count <= minIndex + 1 && count <= 1)
            return;
        if (count <= 1)
            return;

        var hit = VisualTreeHelper.HitTest(hitRoot, listPos);
        int rowIndex = TryGetRowIndexFromSource(hit?.VisualHit, rows);
        FrameworkElement? rowEl = rowIndex >= 0
            ? FindRowElementAtIndex(itemsControl, rows, rowIndex)
            : null;

        if (rowEl == null || rowIndex < 0)
        {
            toIndex = Math.Max(minIndex, count - 1);
            AlignDropLineToIndex(itemsControl, rows, hitRoot, ref toIndex, ref lineY, count, minIndex);
            if (lineY <= 0 && hitRoot is FrameworkElement fe)
                lineY = Math.Clamp(listPos.Y, 1, Math.Max(1, fe.ActualHeight - 2));
            return;
        }

        Point topLeft = rowEl.TranslatePoint(new Point(0, 0), hitRoot);
        double bottom = topLeft.Y + rowEl.ActualHeight;
        double mid = topLeft.Y + rowEl.ActualHeight * 0.5;

        if (listPos.Y < mid)
        {
            toIndex = rowIndex;
            lineY = topLeft.Y;
        }
        else
        {
            toIndex = rowIndex + 1;
            lineY = bottom;
        }

        AlignDropLineToIndex(itemsControl, rows, hitRoot, ref toIndex, ref lineY, count, minIndex);
        if (hitRoot is FrameworkElement rootFe)
        {
            double maxY = Math.Max(0, rootFe.ActualHeight - 2);
            lineY = Math.Clamp(lineY, 0, maxY);
        }
    }

    public static void AlignDropLineToIndex(
        ItemsControl itemsControl,
        ObservableCollection<SongRowViewModel> rows,
        UIElement relativeTo,
        ref int toIndex,
        ref double lineY,
        int count,
        int minIndex)
    {
        toIndex = Math.Max(minIndex, Math.Min(toIndex, count - 1));
        var itemAt = FindRowElementAtIndex(itemsControl, rows, toIndex);
        if (itemAt != null)
            lineY = itemAt.TranslatePoint(new Point(0, 0), relativeTo).Y;
        else if (toIndex > minIndex)
        {
            var prev = FindRowElementAtIndex(itemsControl, rows, toIndex - 1);
            if (prev != null)
            {
                var tl = prev.TranslatePoint(new Point(0, 0), relativeTo);
                lineY = tl.Y + prev.ActualHeight;
            }
        }
    }
}
