using System.Linq;
using Archipolygo.Models;

namespace Archipolygo.ViewModels;

/// <summary>
/// Pieces of a collapsed filter bar's one-line summary
/// (Kompakteres-Layout.md, Teil D) - each returns null for a
/// filter's "show everything" value, so the summary only names what actually
/// narrows the list. Shared by <see cref="GroupViewModel"/> and
/// <see cref="DashboardViewModel"/>; labels match the filter buttons' own.
/// </summary>
public static class FilterSummaryText
{
    public static string Join(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));

    public static string? Relevance(EventRelevanceFilter filter) => filter switch
    {
        EventRelevanceFilter.ConcernsMe => "Concerns me",
        EventRelevanceFilter.FoundByMe => "Found by me",
        _ => null
    };

    public static string? Category(EventCategoryFilter filter) => filter switch
    {
        EventCategoryFilter.Hints => "Hints",
        EventCategoryFilter.Items => "Items",
        EventCategoryFilter.Chat => "Chat",
        _ => null
    };

    public static string? ItemCategory(ItemCategoryFilter filter) => filter switch
    {
        ItemCategoryFilter.Progress => "Progress",
        ItemCategoryFilter.Useful => "Useful",
        ItemCategoryFilter.Normal => "Filler",
        ItemCategoryFilter.Trap => "Trap",
        _ => null
    };

    public static string? ItemClasses(IEventItemClassFilter filter) =>
        EventItemClassFilterText.IsActive(filter) ? EventItemClassFilterText.ButtonText(filter) : null;

    public static string? HintFound(HintFilter filter) => filter == HintFilter.Unfound ? "Unfound" : null;

    public static string? HintRole(HintRoleFilter filter) => filter switch
    {
        HintRoleFilter.IFind => "My location",
        HintRoleFilter.IReceive => "My item",
        _ => null
    };

    public static string? Slot(SlotProfile? slot) => slot?.DisplayName;

    public static string? Search(string? text) => string.IsNullOrWhiteSpace(text) ? null : $"\"{text.Trim()}\"";
}
