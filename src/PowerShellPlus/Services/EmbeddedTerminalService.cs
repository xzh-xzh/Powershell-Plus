using PowerShellPlus.Controls;

namespace PowerShellPlus.Services;

/// <summary>
/// 嵌入式终端服务 - 管理嵌入的 PowerShell 控制台
/// </summary>
public class EmbeddedTerminalService : IDisposable
{
    private EmbeddedConsoleHost? _consoleHost;
    private bool _isDisposed;

    public event EventHandler? ProcessExited;

    public bool IsRunning => _consoleHost != null;

    /// <summary>
    /// 获取控制台宿主控件
    /// </summary>
    public EmbeddedConsoleHost? ConsoleHost => _consoleHost;

    /// <summary>
    /// 创建并返回嵌入的控制台控件
    /// </summary>
    public EmbeddedConsoleHost CreateConsole()
    {
        if (_consoleHost != null)
        {
            _consoleHost.Dispose();
        }

        _consoleHost = new EmbeddedConsoleHost();
        _consoleHost.ProcessExited += (s, e) =>
        {
            ProcessExited?.Invoke(this, EventArgs.Empty);
        };

        return _consoleHost;
    }

    /// <summary>
    /// 发送命令到终端
    /// </summary>
    public void SendCommand(string command)
    {
        _consoleHost?.SendCommand(command);
    }

    /// <summary>
    /// 发送 Ctrl+C 中断
    /// </summary>
    public void SendCtrlC()
    {
        _consoleHost?.SendCtrlC();
    }

    /// <summary>
    /// 聚焦到终端
    /// </summary>
    public void Focus()
    {
        _consoleHost?.FocusConsole();
    }

    /// <summary>
    /// 调整终端大小
    /// </summary>
    public void Resize(int width, int height)
    {
        _consoleHost?.ResizeConsole(width, height);
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        _consoleHost?.Dispose();
        _consoleHost = null;
        _isDisposed = true;

        GC.SuppressFinalize(this);
    }
}




