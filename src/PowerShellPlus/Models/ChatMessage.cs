using CommunityToolkit.Mvvm.ComponentModel;

namespace PowerShellPlus.Models;

public partial class ChatMessage : ObservableObject
{
    [ObservableProperty]
    private string _role = string.Empty; // "user", "assistant", or "system"

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private string? _generatedCommand;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private DateTime _timestamp = DateTime.Now;

    [ObservableProperty]
    private bool _isCommandExecuted;

    [ObservableProperty]
    private string? _executionResult;

    public bool IsUser => Role == "user";
    public bool IsAssistant => Role == "assistant";
    public bool IsSystem => Role == "system";
    public bool HasCommand => !string.IsNullOrWhiteSpace(GeneratedCommand);
}

/// <summary>
/// PowerShell 终端上下文信息
/// </summary>
public class TerminalContext
{
    /// <summary>
    /// 当前工作目录
    /// </summary>
    public string CurrentDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 最近的终端输出（最后N行）
    /// </summary>
    public string RecentOutput { get; set; } = string.Empty;

    /// <summary>
    /// 最近执行的命令
    /// </summary>
    public string? LastCommand { get; set; }

    /// <summary>
    /// 终端是否就绪
    /// </summary>
    public bool IsReady { get; set; }

    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"当前工作目录: {CurrentDirectory}");
        
        if (!string.IsNullOrWhiteSpace(LastCommand))
        {
            sb.AppendLine($"最近执行的命令: {LastCommand}");
        }
        
        if (!string.IsNullOrWhiteSpace(RecentOutput))
        {
            sb.AppendLine("最近的终端输出:");
            sb.AppendLine("```");
            sb.AppendLine(RecentOutput.Trim());
            sb.AppendLine("```");
        }
        
        return sb.ToString();
    }
}
