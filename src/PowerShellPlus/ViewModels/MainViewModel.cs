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
    private readonly PowerShellService _psService;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _userInput = string.Empty;

    [ObservableProperty]
    private string _terminalOutput = string.Empty;

    [ObservableProperty]
    private string _currentDirectory = string.Empty;

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

    public MainViewModel()
    {
        Settings = AppSettings.Load();
        _aiService = new OpenAIService();
        _psService = new PowerShellService();

        _currentDirectory = _psService.CurrentDirectory;
        _isApiConfigured = _aiService.IsConfigured;

        // 订阅 PowerShell 事件
        _psService.OutputReceived += (s, output) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                TerminalOutput += output + Environment.NewLine;
            });
        };

        _psService.ErrorReceived += (s, error) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                TerminalOutput += $"[错误] {error}" + Environment.NewLine;
            });
        };

        _psService.DirectoryChanged += (s, dir) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                CurrentDirectory = dir;
            });
        };

        _psService.CommandCompleted += (s, e) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsExecuting = false;
            });
        };

        // 订阅清屏事件
        _psService.ClearRequested += (s, e) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                TerminalOutput = $"PowerShell Plus - AI 增强终端{Environment.NewLine}";
                TerminalOutput += $"================================{Environment.NewLine}";
                TerminalOutput += $"{_psService.GetPrompt()} ";
            });
        };

        // 初始化快捷命令
        InitializeQuickCommands();

        // 初始欢迎消息
        TerminalOutput = $"PowerShell Plus - AI 增强终端{Environment.NewLine}";
        TerminalOutput += $"================================{Environment.NewLine}";
        TerminalOutput += $"{_psService.GetPrompt()} ";
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
            new() { Name = "清空屏幕", Command = "Clear-Host", Icon = "🧹", IsBuiltIn = true, Description = "清空终端屏幕" },
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
            Content = "正在分析...",
            IsLoading = true
        };
        ChatHistory.Add(aiChat);

        IsGenerating = true;
        GeneratedCommand = string.Empty;
        HasGeneratedCommand = false;

        try
        {
            _cts = new CancellationTokenSource();
            var command = await _aiService.GenerateCommandAsync(userMessage, CurrentDirectory, _cts.Token);

            aiChat.Content = "已生成命令:";
            aiChat.GeneratedCommand = command;
            aiChat.IsLoading = false;

            GeneratedCommand = command;
            HasGeneratedCommand = true;
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
    private async Task ExecuteCommand()
    {
        if (string.IsNullOrWhiteSpace(GeneratedCommand) || IsExecuting)
            return;

        IsExecuting = true;
        var result = await _psService.ExecuteCommandAsync(GeneratedCommand);
        // 如果不是清屏命令（有返回内容），添加提示符
        if (!string.IsNullOrEmpty(result))
        {
            TerminalOutput += $"{_psService.GetPrompt()} ";
        }
    }

    [RelayCommand]
    private async Task ExecuteQuickCommand(CommandTemplate? template)
    {
        if (template == null || IsExecuting)
            return;

        IsExecuting = true;
        GeneratedCommand = template.Command;
        HasGeneratedCommand = true;
        var result = await _psService.ExecuteCommandAsync(template.Command);
        if (!string.IsNullOrEmpty(result))
        {
            TerminalOutput += $"{_psService.GetPrompt()} ";
        }
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
    private async Task ExecuteDirectCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command) || IsExecuting)
            return;

        IsExecuting = true;
        var result = await _psService.ExecuteCommandAsync(command);
        if (!string.IsNullOrEmpty(result))
        {
            TerminalOutput += $"{_psService.GetPrompt()} ";
        }
    }

    [RelayCommand]
    private void ClearTerminal()
    {
        TerminalOutput = $"{_psService.GetPrompt()} ";
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
        // 保存所有命令到设置（都作为自定义命令保存，因为用户可能修改了内置命令）
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
        _psService.Dispose();
    }
}

