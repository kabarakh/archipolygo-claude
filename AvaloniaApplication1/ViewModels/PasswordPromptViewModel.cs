using System.Collections.Generic;
using System.Collections.ObjectModel;
using Archipolygo.Models;

namespace Archipolygo.ViewModels;

/// <summary>
/// Backs <see cref="Views.PasswordPromptWindow"/> - the consolidated
/// "passwords needed to connect" dialog from Feature-Plaene/Passwort-Speicherung.md.
/// Built fresh by <see cref="MainWindowViewModel.HandlePasswordRequestedAsync"/>
/// every time it's shown (covering every group/slot that currently needs a
/// password, not just whichever one triggered this round) rather than kept
/// as one long-lived instance across the whole app session.
/// </summary>
public partial class PasswordPromptViewModel : ViewModelBase
{
    public ObservableCollection<PasswordPromptGroupRow> Groups { get; } = new();

    public static PasswordPromptViewModel For(IReadOnlyList<PasswordPromptGroupRow> groups)
    {
        var viewModel = new PasswordPromptViewModel();
        foreach (var group in groups)
        {
            viewModel.Groups.Add(group);
        }

        return viewModel;
    }
}
