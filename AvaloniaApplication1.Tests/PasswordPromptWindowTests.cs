using System.Linq;
using Archipolygo.Models;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie C (see CLAUDE.md's test-category breakdown): Feature-Plaene/Passwort-Speicherung.md's
/// consolidated password-prompt dialog, against the real <c>.axaml</c> and a
/// real layout pass - same style as <see cref="ConnectionEditorWindowProgressTests"/>.
/// </summary>
public class PasswordPromptWindowTests
{
    [AvaloniaFact]
    public void SingleSlotGroup_ShowsOnlyTheGroupField_NoNestedSlotRow()
    {
        var group = new ServerConnectionGroup { Name = "Server ABC", Host = "host", Port = 1 };
        var slot = new SlotProfile { GroupId = group.Id, SlotName = "Alice", RequiresPassword = true };
        group.Slots.Add(slot);

        var viewModel = PasswordPromptViewModel.For(new[]
        {
            new PasswordPromptGroupRow { Group = group, Slots = new[] { new PasswordPromptSlotRow { Slot = slot } } }
        });

        var window = new PasswordPromptWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var groupNameLabel = window.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == "Server ABC");
        Assert.NotNull(groupNameLabel);

        var slotNameLabel = window.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == "Alice password");
        Assert.True(slotNameLabel is null || !slotNameLabel.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void MultiSlotGroup_SlotFieldsCollapsedByDefault_ExpandedByClickingTheLockToggle()
    {
        var group = new ServerConnectionGroup { Name = "Server B", Host = "host", Port = 1 };
        var slotA = new SlotProfile { GroupId = group.Id, SlotName = "Slot A", RequiresPassword = true };
        var slot12 = new SlotProfile { GroupId = group.Id, SlotName = "Slot 12", RequiresPassword = true };
        group.Slots.Add(slotA);
        group.Slots.Add(slot12);

        var viewModel = PasswordPromptViewModel.For(new[]
        {
            new PasswordPromptGroupRow
            {
                Group = group,
                Slots = new[] { new PasswordPromptSlotRow { Slot = slotA }, new PasswordPromptSlotRow { Slot = slot12 } }
                // SlotFieldsExpanded left at its default (false) - most
                // rooms share one password per slot (dev feedback), so this
                // must stay collapsed until the user actually asks for it.
            }
        });

        var window = new PasswordPromptWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // An invisible ItemsControl doesn't even realize its item containers
        // (unlike a plain collapsed panel - see ConnectionEditorWindowProgressTests,
        // whose rows always exist, just hidden), so nothing to find here yet.
        Assert.Empty(window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Text == "Slot A password" || t.Text == "Slot 12 password"));

        var lockToggle = window.GetVisualDescendants().OfType<ToggleButton>().Single();
        lockToggle.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        var slotLabels = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Text == "Slot A password" || t.Text == "Slot 12 password")
            .ToList();
        Assert.Equal(2, slotLabels.Count);
        Assert.All(slotLabels, t => Assert.True(t.IsEffectivelyVisible));
    }

    [AvaloniaFact]
    public void MultiSlotGroup_PreExpandedForARetryError_ShowsSlotFieldsWithoutAnyClick()
    {
        var group = new ServerConnectionGroup { Name = "Server B", Host = "host", Port = 1 };
        var slotA = new SlotProfile { GroupId = group.Id, SlotName = "Slot A", RequiresPassword = true };
        var slot12 = new SlotProfile { GroupId = group.Id, SlotName = "Slot 12", RequiresPassword = true, Password = "wrong" };
        group.Slots.Add(slotA);
        group.Slots.Add(slot12);

        var slotRows = new[]
        {
            new PasswordPromptSlotRow { Slot = slotA },
            new PasswordPromptSlotRow { Slot = slot12, ShowError = true }
        };
        var viewModel = PasswordPromptViewModel.For(new[]
        {
            new PasswordPromptGroupRow
            {
                Group = group,
                Slots = slotRows,
                SlotFieldsExpanded = PasswordPromptGroupRow.ShouldStartExpanded(slotRows)
            }
        });

        var window = new PasswordPromptWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Both slot rows render their own "Wrong password" TextBlock (only
        // slot12's is actually shown - see ShowError) - narrow down to the
        // one that's really on screen rather than assuming there's only one
        // in the tree at all.
        var visibleErrorLabels = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Text == "Wrong password" && t.IsEffectivelyVisible)
            .ToList();
        Assert.Single(visibleErrorLabels);
    }

    [Fact]
    public void ShouldStartExpanded_TrueOnlyWhenAnySlotHasAnError()
    {
        var group = new ServerConnectionGroup { Name = "S", Host = "h", Port = 1 };
        var slot1 = new SlotProfile { GroupId = group.Id, SlotName = "A" };
        var slot2 = new SlotProfile { GroupId = group.Id, SlotName = "B" };

        Assert.False(PasswordPromptGroupRow.ShouldStartExpanded(new[]
        {
            new PasswordPromptSlotRow { Slot = slot1 }, new PasswordPromptSlotRow { Slot = slot2 }
        }));

        Assert.True(PasswordPromptGroupRow.ShouldStartExpanded(new[]
        {
            new PasswordPromptSlotRow { Slot = slot1 }, new PasswordPromptSlotRow { Slot = slot2, ShowError = true }
        }));
    }

    [AvaloniaFact]
    public void TypingIntoASlotField_WritesStraightThroughToSlotProfilePassword()
    {
        var group = new ServerConnectionGroup { Name = "Server ABC", Host = "host", Port = 1 };
        var slot = new SlotProfile { GroupId = group.Id, SlotName = "Alice", RequiresPassword = true };
        group.Slots.Add(slot);

        var viewModel = PasswordPromptViewModel.For(new[]
        {
            new PasswordPromptGroupRow { Group = group, Slots = new[] { new PasswordPromptSlotRow { Slot = slot } } }
        });

        var window = new PasswordPromptWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The slot's own field is collapsed (ShowSlotToggle false for a
        // single-slot group - see PasswordPromptGroupRow's own doc comment)
        // but still exists, invisible, in the visual tree - filter down to
        // what's actually rendered, same convention as
        // ConnectionEditorWindowProgressTests.
        var passwordBox = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.IsEffectivelyVisible);
        passwordBox.Text = "typed-in-password";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("typed-in-password", group.Password);
    }
}
