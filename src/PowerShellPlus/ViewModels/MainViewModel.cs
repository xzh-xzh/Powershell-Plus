using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerShellPlus.Models;
using PowerShellPlus.Services;

namespace PowerShellPlus.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly OpenAIService _aiService;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _userInput = string.Empty;

    [ObservableProperty]
    private string _generatedCommand = string.Empty;

    [ObservableProperty]
    private bool _isGenerating;

    [ObservableProperty]
    private bool _hasGeneratedCommand;

    [ObservableProperty]
    private bool _isExecuting;

    [ObservableProperty]
    private bool _isApiConfigured;

    [ObservableProperty]
    private AppSettings _settings;

    public ObservableCollection<ChatMessage> ChatHistory { get; } = new();
    public ObservableCollection<CommandTemplate> QuickCommands { get; } = new();

    /// <summary>
    /// 在终端中执行命令的回调
    /// </summary>
    public Action<string>? ExecuteInTerminal { get; set; }

    /// <summary>
    /// 获取终端上下文的回调
    /// </summary>
    public Func<TerminalContext>? GetTerminalContext { get; set; }

    public MainViewModel()
    {
        Settings = AppSettings.Load();
        _aiService = new OpenAIService();

        _isApiConfigured = _aiService.IsConfigured;

        // 初始化快捷命令
        InitializeQuickCommands();

        // 添加欢迎消息
        AddWelcomeMessage();
    }

    private void AddWelcomeMessage()
    {
        ChatHistory.Add(new ChatMessage
        {
            Role = "assistant",
            Content = "你好！我是你的 PowerShell AI 助手。\n\n" +
                     "我可以帮助你：\n" +
                     "• 生成和执行 PowerShell 命令\n" +
                     "• 回答关于 PowerShell 和系统管理的问题\n" +
                     "• 分析终端输出和解决错误\n\n" +
                     "直接告诉我你想做什么，或者问我任何问题！"
        });
    }

    private void InitializeQuickCommands()
    {
        // 如果用户已保存命令配置，直接加载
        if (Settings.CustomCommands.Count > 0)
        {
            foreach (var cmd in Settings.CustomCommands)
            {
                QuickCommands.Add(cmd);
            }
            return;
        }

        // 否则初始化默认内置命令
        var defaultCommands = new List<CommandTemplate>
        {
            new() { Name = "系统信息", Command = "Get-ComputerInfo | Select-Object WindowsVersion, OsName, CsProcessors", Icon = "💻", IsBuiltIn = true, Description = "显示系统基本信息" },
            new() { Name = "磁盘空间", Command = "Get-PSDrive -PSProvider FileSystem | Select-Object Name, @{N='Used(GB)';E={[math]::Round($_.Used/1GB,2)}}, @{N='Free(GB)';E={[math]::Round($_.Free/1GB,2)}}", Icon = "💾", IsBuiltIn = true, Description = "显示磁盘使用情况" },
            new() { Name = "网络状态", Command = "Test-Connection -ComputerName baidu.com -Count 2", Icon = "🌐", IsBuiltIn = true, Description = "测试网络连接" },
            new() { Name = "进程列表", Command = "Get-Process | Sort-Object CPU -Descending | Select-Object -First 10 Name, CPU, WorkingSet64", Icon = "📊", IsBuiltIn = true, Description = "显示CPU占用最高的10个进程" },
            new() { Name = "清空屏幕", Command = "cls", Icon = "🧹", IsBuiltIn = true, Description = "清空终端屏幕" },
            new() { Name = "目录", Command = "Get-ChildItem | Format-Table -AutoSize", Icon = "📁", IsBuiltIn = true, Description = "列出当前目录内容" },
            new() { Name = "conda环境", Command = "conda env list", Icon = "🐍", IsBuiltIn = true, Description = "列出所有 Conda 环境" },
        };

        foreach (var cmd in defaultCommands)
        {
            QuickCommands.Add(cmd);
        }
    }

    [RelayCommand]
    private async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(UserInput) || IsGenerating)
            return;

        var userMessage = UserInput.Trim();
        UserInput = string.Empty;

        // 添加用户消息到历史
        var userChat = new ChatMessage
        {
            Role = "user",
            Content = userMessage
        };
        ChatHistory.Add(userChat);

        // 创建 AI 响应占位
        var aiChat = new ChatMessage
        {
            Role = "assistant",
            Content = "正在思考...",
            IsLoading = true
        };
        ChatHistory.Add(aiChat);

        IsGenerating = true;
        GeneratedCommand = string.Empty;
        HasGeneratedCommand = false;

        try
        {
            _cts = new CancellationTokenSource();
            
            // 获取终端上下文
            var terminalContext = GetTerminalContext?.Invoke() ?? new TerminalContext
            {
                CurrentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                IsReady = false
            };

            // 使用对话模式发送消息
            var response = await _aiService.SendChatAsync(
                userMessage, 
                ChatHistory.Where(m => m != aiChat), // 排除当前正在生成的占位消息
                terminalContext,
                _cts.Token);

            aiChat.Content = response.Content;
            aiChat.IsLoading = false;

            if (response.HasCommand && !string.IsNullOrWhiteSpace(response.Command))
            {
                aiChat.GeneratedCommand = response.Command;
                GeneratedCommand = response.Command;
                HasGeneratedCommand = true;
            }
        }
        catch (Exception ex)
        {
            aiChat.Content = $"生成失败: {ex.Message}";
            aiChat.IsLoading = false;
        }
        finally
        {
            IsGenerating = false;
        }
    }

    [RelayCommand]
    private void ExecuteCommand()
    {
        if (string.IsNullOrWhiteSpace(GeneratedCommand))
            return;

        ExecuteInTerminal?.Invoke(GeneratedCommand);

        // 标记最后一条带命令的消息为已执行
        var lastCommandMessage = ChatHistory.LastOrDefault(m => m.HasCommand && m.GeneratedCommand == GeneratedCommand);
        if (lastCommandMessage != null)
        {
            lastCommandMessage.IsCommandExecuted = true;
        }
    }

    [RelayCommand]
    private void ExecuteMessageCommand(ChatMessage? message)
    {
        if (message == null || !message.HasCommand || string.IsNullOrWhiteSpace(message.GeneratedCommand))
            return;

        ExecuteInTerminal?.Invoke(message.GeneratedCommand);
        message.IsCommandExecuted = true;
        
        // 同步到预览区
        GeneratedCommand = message.GeneratedCommand;
        HasGeneratedCommand = true;
    }

    [RelayCommand]
    private void ExecuteQuickCommand(CommandTemplate? template)
    {
        if (template == null)
            return;

        GeneratedCommand = template.Command;
        HasGeneratedCommand = true;
        
        ExecuteInTerminal?.Invoke(template.Command);
    }

    [RelayCommand]
    private void CopyCommand()
    {
        if (!string.IsNullOrWhiteSpace(GeneratedCommand))
        {
            Clipboard.SetText(GeneratedCommand);
        }
    }

    [RelayCommand]
    private void CopyMessageCommand(ChatMessage? message)
    {
        if (message != null && message.HasCommand && !string.IsNullOrWhiteSpace(message.GeneratedCommand))
        {
            Clipboard.SetText(message.GeneratedCommand);
        }
    }

    [RelayCommand]
    private void ExecuteDirectCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        ExecuteInTerminal?.Invoke(command);
    }

    [RelayCommand]
    private void ClearChat()
    {
        ChatHistory.Clear();
        GeneratedCommand = string.Empty;
        HasGeneratedCommand = false;
        
        // 重新添加欢迎消息
        AddWelcomeMessage();
    }

    [RelayCommand]
    private void NewChat()
    {
        ClearChat();
    }

    public void UpdateSettings(AppSettings newSettings)
    {
        Settings = newSettings;
        Settings.Save();
        _aiService.UpdateSettings(newSettings);
        IsApiConfigured = _aiService.IsConfigured;

        // 更新快捷命令
        RefreshCustomCommands(newSettings.CustomCommands);
    }

    public void UpdateCustomCommands(List<CommandTemplate> commands)
    {
        Settings.CustomCommands = commands;
        Settings.Save();
        RefreshCustomCommands(commands);
    }

    public void UpdateAllCommands(List<CommandTemplate> allCommands)
    {
        // 保存所有命令到设置
        Settings.CustomCommands = allCommands;
        Settings.Save();

        // 刷新 UI
        QuickCommands.Clear();
        foreach (var cmd in allCommands)
        {
            QuickCommands.Add(cmd);
        }
    }

    private void RefreshCustomCommands(List<CommandTemplate> commands)
    {
        // 移除旧的自定义命令
        var customCommands = QuickCommands.Where(c => !c.IsBuiltIn).ToList();
        foreach (var cmd in customCommands)
        {
            QuickCommands.Remove(cmd);
        }
        // 添加新的自定义命令
        foreach (var cmd in commands)
        {
            QuickCommands.Add(cmd);
        }
    }

    public void Cleanup()
    {
        _cts?.Cancel();
    }
}
