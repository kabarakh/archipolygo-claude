using System.Collections.Generic;
using System.Collections.ObjectModel;
using Archipolygo.Models;

namespace Archipolygo.ViewModels;

/// <summary>
/// Consolidated "passwords needed to connect" dialog. Built fresh each time it's shown, covering every
/// group/slot that currently needs a password, not just the one that triggered it.
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
