using System.Windows;
using PowerShellPlus.Models;

namespace PowerShellPlus.Views;

public partial class SettingsWindow : Window
{
    public AppSettings Settings { get; private set; }

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        Settings = new AppSettings
        {
            ApiKey = settings.ApiKey,
            ApiBaseUrl = settings.ApiBaseUrl,
            Model = settings.Model,
            Temperature = settings.Temperature,
            MaxTokens = settings.MaxTokens,
            CustomCommands = settings.CustomCommands // 保留原有命令
        };

        // 初始化控件值
        ApiKeyBox.Password = Settings.ApiKey;
        ApiBaseUrlBox.Text = Settings.ApiBaseUrl;
        ModelBox.Text = Settings.Model;
        TemperatureSlider.Value = Settings.Temperature;
        MaxTokensBox.Text = Settings.MaxTokens.ToString();
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        Settings.ApiKey = ApiKeyBox.Password;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // 验证并保存设置
        Settings.ApiBaseUrl = ApiBaseUrlBox.Text.Trim();
        Settings.Model = ModelBox.Text.Trim();
        Settings.Temperature = TemperatureSlider.Value;

        if (int.TryParse(MaxTokensBox.Text, out int maxTokens))
        {
            Settings.MaxTokens = maxTokens;
        }

        // 基本验证
        if (string.IsNullOrWhiteSpace(Settings.ApiBaseUrl))
        {
            MessageBox.Show("请输入 API Base URL", "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(Settings.Model))
        {
            MessageBox.Show("请输入模型名称", "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
