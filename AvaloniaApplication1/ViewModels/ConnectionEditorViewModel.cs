using System;
using System.Collections.Generic;
using System.Linq;
using Archipolygo.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.ViewModels;

/// <summary>
/// Editor view model for creating or editing a <see cref="ServerProfile"/>.
/// Shown as a dialog by <see cref="Views.ConnectionEditorWindow"/>.
/// </summary>
public partial class ConnectionEditorViewModel : ViewModelBase
{
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

    [ObservableProperty]
    private string? _validationError;

    /// <summary>
    /// Id of the profile being edited, if in edit mode; otherwise null (= new profile).
    /// </summary>
    public Guid? EditingProfileId { get; private set; }

    /// <summary>
    /// Other profiles already in use, against which <see cref="TryBuildProfile"/>
    /// checks for an exact Host+Port+SlotName duplicate. Does not include the
    /// profile currently being edited (if any).
    /// </summary>
    private IReadOnlyList<ServerProfile> _existingProfiles = Array.Empty<ServerProfile>();

    public static ConnectionEditorViewModel ForNewProfile(bool defaultAutoConnect = false, IReadOnlyList<ServerProfile>? existingProfiles = null) => new()
    {
        AutoConnect = defaultAutoConnect,
        _existingProfiles = existingProfiles ?? Array.Empty<ServerProfile>()
    };

    public static ConnectionEditorViewModel ForExistingProfile(ServerProfile profile, IReadOnlyList<ServerProfile>? existingProfiles = null) => new()
    {
        EditingProfileId = profile.Id,
        Name = profile.Name,
        Host = profile.Host,
        Port = profile.Port,
        SlotName = profile.SlotName,
        Password = profile.Password,
        AutoConnect = profile.AutoConnect,
        _existingProfiles = existingProfiles ?? Array.Empty<ServerProfile>()
    };

    /// <summary>
    /// Pre-fills the editor from an existing profile to create a copy of it,
    /// as a brand-new profile (no <see cref="EditingProfileId"/>). Since the
    /// copy starts out identical to its source, <see cref="TryBuildProfile"/>
    /// refuses to save it until the user changes at least the host, port or
    /// slot name, so two profiles can never end up with exactly the same
    /// connection data.
    /// </summary>
    public static ConnectionEditorViewModel ForDuplicateProfile(ServerProfile source, IReadOnlyList<ServerProfile>? existingProfiles = null) => new()
    {
        Name = $"{source.Name} (Copy)",
        Host = source.Host,
        Port = source.Port,
        SlotName = source.SlotName,
        Password = source.Password,
        AutoConnect = source.AutoConnect,
        _existingProfiles = existingProfiles ?? Array.Empty<ServerProfile>()
    };

    /// <summary>
    /// Validates the input and returns a <see cref="ServerProfile"/> on success.
    /// </summary>
    public bool TryBuildProfile(out ServerProfile profile)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationError = "Name must not be empty.";
            profile = null!;
            return false;
        }

        if (string.IsNullOrWhiteSpace(Host))
        {
            ValidationError = "Host must not be empty.";
            profile = null!;
            return false;
        }

        if (Port is <= 0 or > 65535)
        {
            ValidationError = "Port must be between 1 and 65535.";
            profile = null!;
            return false;
        }

        var trimmedHost = Host.Trim();
        var trimmedSlotName = SlotName.Trim();

        var isDuplicate = _existingProfiles.Any(p =>
            p.Id != EditingProfileId &&
            string.Equals(p.Host, trimmedHost, StringComparison.OrdinalIgnoreCase) &&
            p.Port == Port &&
            string.Equals(p.SlotName, trimmedSlotName, StringComparison.OrdinalIgnoreCase));

        if (isDuplicate)
        {
            ValidationError = "A connection with the same host, port and slot name already exists.";
            profile = null!;
            return false;
        }

        ValidationError = null;
        profile = new ServerProfile
        {
            Id = EditingProfileId ?? Guid.NewGuid(),
            Name = Name.Trim(),
            Host = trimmedHost,
            Port = Port,
            SlotName = trimmedSlotName,
            Password = Password,
            AutoConnect = AutoConnect
        };
        return true;
    }
}
