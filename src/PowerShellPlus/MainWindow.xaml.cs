using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PowerShellPlus.Models;
using PowerShellPlus.ViewModels;
using PowerShellPlus.Views;

namespace PowerShellPlus;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        // 设置终端命令执行回调
        _viewModel.ExecuteInTerminal = ExecuteCommandInTerminal;

        // 设置获取终端上下文回调
        _viewModel.GetTerminalContext = GetTerminalContext;

        // 监听对话历史变化，自动滚动到底部
        _viewModel.ChatHistory.CollectionChanged += (s, e) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                ChatScrollViewer.ScrollToEnd();
            }, System.Windows.Threading.DispatcherPriority.Background);
        };
    }

    private TerminalContext GetTerminalContext()
    {
        return new TerminalContext
        {
            CurrentDirectory = TerminalControl.GetCurrentDirectory(),
            RecentOutput = TerminalControl.GetRecentOutput(20),
            LastCommand = TerminalControl.LastCommand,
            IsReady = TerminalControl.IsReady
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // 终端会自动初始化，无需手动操作
        UpdateStatus("正在初始化终端...", false);
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _viewModel.Cleanup();
        TerminalControl.Dispose();
    }

    private void TerminalControl_TerminalReady(object? sender, EventArgs e)
    {
        UpdateStatus("终端就绪 | 点击终端区域开始输入", true);
    }

    private void TerminalControl_TitleChanged(object? sender, string title)
    {
        // 更新标题栏
        if (!string.IsNullOrEmpty(title))
        {
            TerminalTitle.Text = title;
        }
    }

    private void TerminalControl_ProcessExited(object? sender, int exitCode)
    {
        UpdateStatus($"终端进程已退出 (代码: {exitCode})", false);
        StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // 红色
    }

    private void UpdateStatus(string message, bool isReady)
    {
        StatusText.Text = message;
        StatusIndicator.Fill = isReady 
            ? new SolidColorBrush(Color.FromRgb(16, 185, 129))  // 绿色
            : new SolidColorBrush(Color.FromRgb(245, 158, 11)); // 橙色
    }

    private void ExecuteCommandInTerminal(string command)
    {
        if (TerminalControl.IsReady)
        {
            TerminalControl.SendCommand(command);
            TerminalControl.FocusTerminal();
        }
    }

    private void ClearTerminal_Click(object sender, RoutedEventArgs e)
    {
        TerminalControl.Clear();
        TerminalControl.ClearRecentOutputBuffer();
    }

    private void InterruptCommand_Click(object sender, RoutedEventArgs e)
    {
        TerminalControl.SendCtrlC();
    }

    private void UserInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(_viewModel.UserInput))
        {
            _viewModel.SendMessageCommand.Execute(null);
        }
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_viewModel.Settings)
        {
            Owner = this
        };

        if (settingsWindow.ShowDialog() == true)
        {
            _viewModel.UpdateSettings(settingsWindow.Settings);
        }
    }

    private void ManageCommands_Click(object sender, RoutedEventArgs e)
    {
        var allCommands = _viewModel.QuickCommands.ToList();

        var managerWindow = new CommandManagerWindow(allCommands)
        {
            Owner = this
        };

        var result = managerWindow.ShowDialog();

        if (result == true || managerWindow.HasChanges)
        {
            _viewModel.UpdateAllCommands(managerWindow.Commands.ToList());
        }
    }
}
