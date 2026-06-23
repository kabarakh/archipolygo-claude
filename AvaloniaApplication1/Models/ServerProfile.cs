using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.Models;

/// <summary>
/// Saved connection profile for an Archipelago server/slot.
/// </summary>
public partial class ServerProfile : ObservableObject
{
    [ObservableProperty]
    private Guid _id = Guid.NewGuid();

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _host = "archipelago.gg";

    [ObservableProperty]
    private int _port = 38281;

    [ObservableProperty]
    private string _slotName = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _autoConnect;
}
