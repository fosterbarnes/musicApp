using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace musicApp.Helpers;

/// <summary>
/// ObservableCollection with a bulk add that raises a single Reset notification
/// instead of one CollectionChanged per item. Bound views (CollectionView,
/// ItemContainerGenerator) reprocess once, which keeps large library loads off
/// the O(n^2) per-item insert path.
/// </summary>
public class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void AddRange(IEnumerable<T> items)
    {
        if (items == null)
            return;

        CheckReentrancy();

        int before = Items.Count;
        foreach (var item in items)
            Items.Add(item);

        if (Items.Count == before)
            return;

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
