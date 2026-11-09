namespace Archipolygo.Models;

/// <summary>An entry of the editor's "Watched slot" picker; a null <see cref="SlotName"/> is "(none)".</summary>
public sealed record WatchedSlotChoice(string? SlotName, string DisplayText);
