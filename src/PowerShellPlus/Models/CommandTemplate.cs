using CommunityToolkit.Mvvm.ComponentModel;

namespace PowerShellPlus.Models;

public partial class CommandTemplate : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _command = string.Empty;

    [ObservableProperty]
    private string _icon = "⚡";

    [ObservableProperty]
    private bool _isBuiltIn;
}

