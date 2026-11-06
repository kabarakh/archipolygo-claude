using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Archipolygo.ViewModels;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that can change many items with one notification, so a burst of
/// thousands of lines costs the bound list one update instead of thousands.
/// See Umsetzungsplan.md, "UI-Updates in Zeitscheiben".
/// </summary>
public class RangeObservableCollection<T> : ObservableCollection<T>
{
    public RangeObservableCollection()
    {
    }

    public RangeObservableCollection(IEnumerable<T> items) : base(items)
    {
    }

    public void AddRange(IReadOnlyList<T> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        if (items.Count == 1)
        {
            Add(items[0]);
            return;
        }

        CheckReentrancy();
        var startIndex = Count;
        foreach (var item in items)
        {
            Items.Add(item);
        }

        RaiseCountAndIndexerChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items.ToList(), startIndex));
    }

    public void RemoveRange(int index, int count)
    {
        if (count <= 0)
        {
            return;
        }

        if (count == 1)
        {
            RemoveAt(index);
            return;
        }

        CheckReentrancy();
        var removed = new List<T>(count);
        for (var i = 0; i < count; i++)
        {
            removed.Add(Items[index]);
            Items.RemoveAt(index);
        }

        RaiseCountAndIndexerChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, index));
    }

    /// <summary>Removes every match with one Reset instead of one notification per removed item.</summary>
    public void RemoveAll(Func<T, bool> predicate)
    {
        if (!Items.Any(predicate))
        {
            return;
        }

        ResetTo(Items.Where(item => !predicate(item)).ToList());
    }

    public void ResetTo(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        RaiseCountAndIndexerChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private void RaiseCountAndIndexerChanged()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
    }
}
