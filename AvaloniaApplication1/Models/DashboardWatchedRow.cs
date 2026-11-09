using Archipolygo.ViewModels;

namespace Archipolygo.Models;

/// <summary>A row of the Dashboard's "Watched only" list: one server's watched slot and one of its progression items.</summary>
public sealed record DashboardWatchedRow(GroupViewModel Group, WatchedItemRow Item);
