using System.Collections.Generic;
using System.Threading.Tasks;
using Archipolygo.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Archipolygo.ViewModels;

/// <summary>The message box: sending, and the Up/Down-arrow recall history.</summary>
public partial class GroupViewModel
{
    [ObservableProperty]
    private string _messageToSend = string.Empty;

    // Terminal-style Up/Down recall. Index -1 = not navigating; the draft is
    // what was typed before the first Up, restored when stepping past the newest.
    private const int MaxMessageHistory = 50;
    private readonly List<string> _messageHistory = new();
    private int _messageHistoryIndex = -1;
    private string? _messageHistoryDraft;

    [RelayCommand(CanExecute = nameof(IsLeaderConnected))]
    private async Task SendMessageAsync()
    {
        var text = MessageToSend;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        MessageToSend = string.Empty;
        RecordSentMessage(text);
        await _connectionManager.SendMessageAsync(this, text);
    }

    /// <summary>No dedupe: resending the same !hint text repeatedly is the main use case.</summary>
    private void RecordSentMessage(string text)
    {
        // A hand-typed "!admin login <pw>" etc. must not be recallable in plain text.
        if (AdminCommands.ContainsPassword(text))
        {
            _messageHistoryIndex = -1;
            _messageHistoryDraft = null;
            return;
        }

        _messageHistory.Add(text);
        if (_messageHistory.Count > MaxMessageHistory)
        {
            _messageHistory.RemoveAt(0);
        }

        _messageHistoryIndex = -1;
        _messageHistoryDraft = null;
    }

    /// <summary>Up arrow; the first press stashes the current draft.</summary>
    public void RecallPreviousMessage()
    {
        if (_messageHistory.Count == 0)
        {
            return;
        }

        if (_messageHistoryIndex == -1)
        {
            _messageHistoryDraft = MessageToSend;
            _messageHistoryIndex = _messageHistory.Count - 1;
        }
        else if (_messageHistoryIndex > 0)
        {
            _messageHistoryIndex--;
        }

        MessageToSend = _messageHistory[_messageHistoryIndex];
    }

    /// <summary>Down arrow; stepping past the newest entry restores the draft.</summary>
    public void RecallNextMessage()
    {
        if (_messageHistoryIndex == -1)
        {
            return;
        }

        if (_messageHistoryIndex < _messageHistory.Count - 1)
        {
            _messageHistoryIndex++;
            MessageToSend = _messageHistory[_messageHistoryIndex];
        }
        else
        {
            _messageHistoryIndex = -1;
            MessageToSend = _messageHistoryDraft ?? string.Empty;
            _messageHistoryDraft = null;
        }
    }
}
