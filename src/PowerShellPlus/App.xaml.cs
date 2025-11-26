using System.Configuration;
using System.Data;
using System.Text;
using System.Windows;

namespace PowerShellPlus;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        // 注册 CodePagesEncodingProvider 以支持 GBK 等编码
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}

