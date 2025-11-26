using System.Windows;
using System.Windows.Controls;
using PowerShellPlus.Models;

namespace PowerShellPlus.Views;

public partial class CommandEditWindow : Window
{
    public CommandTemplate? Command { get; private set; }
    private readonly bool _isEditMode;

    public CommandEditWindow(CommandTemplate? existingCommand)
    {
        InitializeComponent();

        _isEditMode = existingCommand != null;

        if (_isEditMode && existingCommand != null)
        {
            DialogTitle.Text = "✏️ 编辑命令";
            Title = "编辑命令";

            // 填充现有数据
            NameBox.Text = existingCommand.Name;
            IconBox.Text = existingCommand.Icon;
            CommandBox.Text = existingCommand.Command;
            DescriptionBox.Text = existingCommand.Description;

            // 保留原有 ID
            Command = new CommandTemplate
            {
                Id = existingCommand.Id,
                IsBuiltIn = false
            };
        }
        else
        {
            DialogTitle.Text = "➕ 添加命令";
            Title = "添加命令";
            IconBox.Text = "⚡"; // 默认图标
            Command = new CommandTemplate { IsBuiltIn = false };
        }

        // 聚焦到名称输入框
        Loaded += (s, e) => NameBox.Focus();
    }

    private void EmojiButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            IconBox.Text = button.Content.ToString();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // 验证
        var name = NameBox.Text.Trim();
        var command = CommandBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("请输入命令名称", "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            NameBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            MessageBox.Show("请输入 PowerShell 命令", "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            CommandBox.Focus();
            return;
        }

        // 设置命令属性
        Command!.Name = name;
        Command.Icon = string.IsNullOrWhiteSpace(IconBox.Text) ? "⚡" : IconBox.Text.Trim();
        Command.Command = command;
        Command.Description = DescriptionBox.Text.Trim();

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

