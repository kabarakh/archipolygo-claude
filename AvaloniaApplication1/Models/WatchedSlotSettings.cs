namespace Archipolygo.Models;

/// <summary>The watched-slot part of an Edit-server result.</summary>
public sealed record WatchedSlotSettings(string? SlotName, int ItemCount, bool CatchUpByLogin);
