using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            var textBox = sender as TextBox;
            var command = textBox?.Text;
            if (!string.IsNullOrWhiteSpace(command))
            {
                _viewModel.ExecuteDirectCommandCommand.Execute(command);
                textBox!.Clear();
            }
        }
    }

    private void ExecuteDirectCommand_Click(object sender, RoutedEventArgs e)
    {
        var command = DirectCommandInput.Text;
        if (!string.IsNullOrWhiteSpace(command))
        {
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
