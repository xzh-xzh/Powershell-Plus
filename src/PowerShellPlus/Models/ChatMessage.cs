using CommunityToolkit.Mvvm.ComponentModel;

namespace PowerShellPlus.Models;

public partial class ChatMessage : ObservableObject
{
    [ObservableProperty]
    private string _role = string.Empty; // "user" or "assistant"

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private string? _generatedCommand;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private DateTime _timestamp = DateTime.Now;

    public bool IsUser => Role == "user";
    public bool IsAssistant => Role == "assistant";
    public bool HasCommand => !string.IsNullOrWhiteSpace(GeneratedCommand);
}

