using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace Archipolygo.ViewModels;

/// <summary>
/// A filtered, order-preserving copy of a source collection that follows the source incrementally: an append
/// only tests the new items, and a trim at the front removes only those. Anything else, and every filter
/// change (<see cref="Refresh"/>), rebuilds it with one Reset. Stays the same instance for its whole life, so a
/// bound ListBox never re-reads the whole list on an append.
/// See Umsetzungsplan.md, "UI-Updates in Zeitscheiben".
/// </summary>
public sealed class FilteredCollection<T> : RangeObservableCollection<T> where T : class
{
    private readonly ObservableCollection<T> _source;
    private readonly Func<T, bool> _predicate;

    public FilteredCollection(ObservableCollection<T> source, Func<T, bool> predicate)
        : base(source.Where(predicate))
    {
        _source = source;
        _predicate = predicate;
        _source.CollectionChanged += OnSourceCollectionChanged;
    }

    public void Refresh() => ResetTo(_source.Where(_predicate).ToList());

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when IsAppendAtEnd(e):
                AddRange(e.NewItems!.Cast<T>().Where(_predicate).ToList());
                break;
            case NotifyCollectionChangedAction.Remove when e.OldStartingIndex == 0:
                RemoveLeadingItems(e.OldItems!);
                break;
            default:
                Refresh();
                break;
        }
    }

    private bool IsAppendAtEnd(NotifyCollectionChangedEventArgs e) =>
        e.NewItems is not null && e.NewStartingIndex == _source.Count - e.NewItems.Count;

    /// <summary>
    /// The source's front items are this list's front items too (same order), so the shown ones among them form a
    /// prefix here. Matches by reference, not the predicate: a filter-relevant state (a goal) may have changed since.
    /// </summary>
    private void RemoveLeadingItems(System.Collections.IList removedFromSource)
    {
        var removed = new HashSet<T>(removedFromSource.Cast<T>(), ReferenceEqualityComparer.Instance);
        var leading = 0;
        while (leading < Count && removed.Contains(this[leading]))
        {
            leading++;
        }

        RemoveRange(0, leading);
    }
}
