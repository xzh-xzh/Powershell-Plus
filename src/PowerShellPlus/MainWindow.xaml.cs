using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PowerShellPlus.ViewModels;
using PowerShellPlus.Views;

namespace PowerShellPlus;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly List<string> _commandHistory = new();
    private int _historyIndex = -1;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
    }

    private void UserInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            if (_viewModel.SendMessageCommand.CanExecute(null))
            {
                _viewModel.SendMessageCommand.Execute(null);
            }
        }
    }

    private void DirectCommandInput_KeyDown(object sender, KeyEventArgs e)
    {
        var textBox = sender as TextBox;
        
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                var command = textBox?.Text;
                if (!string.IsNullOrWhiteSpace(command))
                {
                    // 添加到历史
                    _commandHistory.Add(command);
                    _historyIndex = _commandHistory.Count;
                    
                    _viewModel.ExecuteDirectCommandCommand.Execute(command);
                    textBox!.Clear();
                }
                break;
                
            case Key.Up:
                // 向上浏览历史
                e.Handled = true;
                if (_commandHistory.Count > 0 && _historyIndex > 0)
                {
                    _historyIndex--;
                    textBox!.Text = _commandHistory[_historyIndex];
                    textBox.CaretIndex = textBox.Text.Length;
                }
                break;
                
            case Key.Down:
                // 向下浏览历史
                e.Handled = true;
                if (_historyIndex < _commandHistory.Count - 1)
                {
                    _historyIndex++;
                    textBox!.Text = _commandHistory[_historyIndex];
                    textBox.CaretIndex = textBox.Text.Length;
                }
                else if (_historyIndex == _commandHistory.Count - 1)
                {
                    _historyIndex = _commandHistory.Count;
                    textBox!.Clear();
                }
                break;
                
            case Key.C:
                // Ctrl+C 中断当前命令
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && string.IsNullOrEmpty(textBox?.SelectedText))
                {
                    e.Handled = true;
                    _viewModel.InterruptCommandCommand.Execute(null);
                }
                break;
                
            case Key.L:
                // Ctrl+L 清屏
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    e.Handled = true;
                    _viewModel.ClearTerminalCommand.Execute(null);
                }
                break;
        }
    }

    private void ExecuteDirectCommand_Click(object sender, RoutedEventArgs e)
    {
        var command = DirectCommandInput.Text;
        if (!string.IsNullOrWhiteSpace(command))
        {
            _commandHistory.Add(command);
            _historyIndex = _commandHistory.Count;
            
            _viewModel.ExecuteDirectCommandCommand.Execute(command);
            DirectCommandInput.Clear();
        }
    }

    private void TerminalOutput_TextChanged(object sender, TextChangedEventArgs e)
    {
        var textBox = sender as TextBox;
        textBox?.ScrollToEnd();
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
        // 传递所有命令（内置+自定义）
        var allCommands = _viewModel.QuickCommands.ToList();
        var managerWindow = new CommandManagerWindow(allCommands)
        {
            Owner = this
        };

        managerWindow.ShowDialog();
        
        if (managerWindow.HasChanges)
        {
            _viewModel.UpdateAllCommands(managerWindow.Commands.ToList());
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Cleanup();
        base.OnClosed(e);
    }
}
