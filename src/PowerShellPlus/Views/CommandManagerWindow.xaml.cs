using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PowerShellPlus.Models;

namespace PowerShellPlus.Views;

public partial class CommandManagerWindow : Window
{
    public ObservableCollection<CommandTemplate> Commands { get; }
    public bool HasChanges { get; private set; }

    public CommandManagerWindow(List<CommandTemplate> allCommands)
    {
        InitializeComponent();
        
        // 深拷贝所有命令
        Commands = new ObservableCollection<CommandTemplate>(
            allCommands.Select(c => new CommandTemplate
            {
                Id = c.Id,
                Name = c.Name,
                Command = c.Command,
                Icon = c.Icon,
                Description = c.Description,
                IsBuiltIn = c.IsBuiltIn
            })
        );
        CommandListBox.ItemsSource = Commands;
        
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = Commands.Count == 0 
            ? Visibility.Visible 
            : Visibility.Collapsed;
    }

    private void AddCommand_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CommandEditWindow(null)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.Command != null)
        {
            Commands.Add(dialog.Command);
            HasChanges = true;
            UpdateEmptyState();
        }
    }

    private void EditCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is CommandTemplate command)
        {
            var dialog = new CommandEditWindow(command)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true && dialog.Command != null)
            {
                var index = Commands.IndexOf(command);
                if (index >= 0)
                {
                    // 保持原有的 IsBuiltIn 状态，但标记为已修改（不再是内置）
                    dialog.Command.IsBuiltIn = false;
                    Commands[index] = dialog.Command;
                    HasChanges = true;
                }
            }
        }
    }

    private void DeleteCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is CommandTemplate command)
        {
            var result = MessageBox.Show(
                $"确定要删除命令「{command.Name}」吗？",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Commands.Remove(command);
                HasChanges = true;
                UpdateEmptyState();
            }
        }
    }

    private void ResetDefault_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "确定要恢复为默认命令吗？\n这将删除所有自定义命令。",
            "确认恢复",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            Commands.Clear();
            
            // 添加默认命令
            var defaultCommands = GetDefaultCommands();
            foreach (var cmd in defaultCommands)
            {
                Commands.Add(cmd);
            }
            
            HasChanges = true;
            UpdateEmptyState();
        }
    }

    private static List<CommandTemplate> GetDefaultCommands()
    {
        return new List<CommandTemplate>
        {
            new() { Name = "系统信息", Command = "Get-ComputerInfo | Select-Object WindowsVersion, OsName, CsProcessors", Icon = "💻", IsBuiltIn = true, Description = "显示系统基本信息" },
            new() { Name = "磁盘空间", Command = "Get-PSDrive -PSProvider FileSystem | Select-Object Name, @{N='Used(GB)';E={[math]::Round($_.Used/1GB,2)}}, @{N='Free(GB)';E={[math]::Round($_.Free/1GB,2)}}", Icon = "💾", IsBuiltIn = true, Description = "显示磁盘使用情况" },
            new() { Name = "网络状态", Command = "Test-Connection -ComputerName baidu.com -Count 2", Icon = "🌐", IsBuiltIn = true, Description = "测试网络连接" },
            new() { Name = "进程列表", Command = "Get-Process | Sort-Object CPU -Descending | Select-Object -First 10 Name, CPU, WorkingSet64", Icon = "📊", IsBuiltIn = true, Description = "显示CPU占用最高的10个进程" },
            new() { Name = "清空屏幕", Command = "Clear-Host", Icon = "🧹", IsBuiltIn = true, Description = "清空终端屏幕" },
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        // 不要在这里设置 DialogResult，避免异常
    }
}
