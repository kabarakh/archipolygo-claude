using System.Collections.Generic;

namespace Archipolygo.ViewModels;

public enum PortableStartupChoice
{
    Quit,
    CopyFromAppData,
    MoveFromAppData,
    StartEmpty,
}

/// <summary>The owner-less dialog shown before the main window exists, for portable-mode setup and its errors.</summary>
public sealed class PortableStartupViewModel : ViewModelBase
{
    public required IReadOnlyList<string> Paragraphs { get; init; }

    /// <summary>False = an error the app can't start past; only "Quit" is offered.</summary>
    public bool ShowTransferChoices { get; init; }

    public bool ShowQuitOnly => !ShowTransferChoices;

    public static PortableStartupViewModel NotWritable(string portableDirectory) => new()
    {
        Paragraphs =
        [
            "Portable mode is on (portable.txt was found), but this folder is not writable:",
            portableDirectory,
            "Move Archipolygo to a folder you can write to, or delete portable.txt to store data in the user profile again.",
        ],
    };

    public static PortableStartupViewModel TransferFailed(string error) => new()
    {
        Paragraphs = ["Copying the existing data failed. Nothing in the user profile was changed.", error],
    };

    public static PortableStartupViewModel OfferTransfer(string appDataDirectory, string portableDirectory) => new()
    {
        Paragraphs =
        [
            "Portable mode is on, and its data folder is empty:",
            portableDirectory,
            "Existing data was found in the user profile:",
            appDataDirectory,
            "What do you want to do with it?",
        ],
        ShowTransferChoices = true,
    };
}
