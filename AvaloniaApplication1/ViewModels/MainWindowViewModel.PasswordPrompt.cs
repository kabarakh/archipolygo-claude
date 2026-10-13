using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;

namespace Archipolygo.ViewModels;

/// <summary>Answers <see cref="IConnectionManager.PasswordRequested"/> with one consolidated password dialog.</summary>
public partial class MainWindowViewModel
{
    /// <summary>
    /// One password dialog at a time: different groups' connects aren't serialized against
    /// each other, so two could otherwise pop up at once.
    /// </summary>
    private readonly SemaphoreSlim _passwordPromptGate = new(1, 1);

    /// <summary>
    /// Set by the view to show the password dialog; returns whether "Connect" was clicked.
    /// Null = no dialog can be shown.
    /// </summary>
    public Func<PasswordPromptViewModel, CancellationToken, Task<bool>>? ShowPasswordPromptDialogAsync { get; set; }

    /// <summary>
    /// Shows one dialog for every slot that currently needs a password, not just
    /// <paramref name="slot"/>. Fields bind straight to the models, so later groups' calls
    /// hit the fast path: several startup calls look like ONE dialog.
    /// </summary>
    private async Task<bool> HandlePasswordRequestedAsync(GroupViewModel group, SlotProfile slot, bool isRetry, CancellationToken cancellationToken)
    {
        // Fast path: an earlier round may already have filled this password in.
        // Skipped on retry, where the current value is known to be wrong.
        if (!isRetry && HasKnownPassword(group, slot))
        {
            return true;
        }

        if (ShowPasswordPromptDialogAsync is null)
        {
            return false;
        }

        try
        {
            await _passwordPromptGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        try
        {
            // Re-check: the round that just held the gate may have resolved this too.
            if (!isRetry && HasKnownPassword(group, slot))
            {
                return true;
            }

            var promptViewModel = BuildPasswordPromptViewModel(group, slot, isRetry);
            var accepted = await ShowPasswordPromptDialogAsync(promptViewModel, cancellationToken);

            // "Connect" only tries filled fields; a slot left blank stays disconnected.
            return accepted && HasKnownPassword(group, slot);
        }
        finally
        {
            _passwordPromptGate.Release();
        }
    }

    private static bool HasKnownPassword(GroupViewModel group, SlotProfile slot) =>
        !string.IsNullOrEmpty(slot.Password) || !string.IsNullOrEmpty(group.Group.Password);

    private static bool NeedsPassword(GroupViewModel group, SlotProfile slot) =>
        slot.RequiresPassword && !HasKnownPassword(group, slot);

    /// <summary>
    /// Collects every slot needing a password. On retry the asking slot is force-included:
    /// its field is filled but wrong, so the scan alone would skip it.
    /// </summary>
    private PasswordPromptViewModel BuildPasswordPromptViewModel(GroupViewModel askingGroup, SlotProfile askingSlot, bool isRetry)
    {
        var groupRows = new List<PasswordPromptGroupRow>();

        foreach (var groupViewModel in Groups)
        {
            var slotsNeeding = groupViewModel.Group.Slots.Where(s => NeedsPassword(groupViewModel, s)).ToList();

            if (isRetry && ReferenceEquals(groupViewModel, askingGroup) && !slotsNeeding.Contains(askingSlot))
            {
                slotsNeeding.Add(askingSlot);
            }

            if (slotsNeeding.Count == 0)
            {
                continue;
            }

            var slotRows = slotsNeeding
                .Select(s => new PasswordPromptSlotRow { Slot = s, ShowError = isRetry && s == askingSlot })
                .ToList();

            groupRows.Add(new PasswordPromptGroupRow
            {
                Group = groupViewModel.Group,
                Slots = slotRows,
                SlotFieldsExpanded = PasswordPromptGroupRow.ShouldStartExpanded(slotRows)
            });
        }

        return PasswordPromptViewModel.For(groupRows);
    }
}
