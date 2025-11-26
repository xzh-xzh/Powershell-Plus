using System.Diagnostics;
using System.IO;
using System.Text;

namespace PowerShellPlus.Services;

/// <summary>
/// 真正的 PowerShell 终端服务，使用进程重定向
/// </summary>
public class TerminalService : IDisposable
{
    private Process? _process;
    private StreamWriter? _inputWriter;
    private bool _isDisposed;
    private readonly StringBuilder _outputBuffer = new();
    private readonly object _lock = new();

    public event EventHandler<string>? OutputReceived;
    public event EventHandler<string>? ErrorReceived;
    public event EventHandler? ProcessExited;

    public bool IsRunning => _process != null && !_process.HasExited;
    public string CurrentDirectory { get; private set; } = string.Empty;

    public TerminalService()
    {
        CurrentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public void Start()
    {
        if (IsRunning) return;

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoLogo -NoExit -Command -",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = CurrentDirectory
        };

        _process = new Process { StartInfo = startInfo };
        _process.OutputDataReceived += OnOutputDataReceived;
        _process.ErrorDataReceived += OnErrorDataReceived;
        _process.Exited += OnProcessExited;
        _process.EnableRaisingEvents = true;

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        _inputWriter = _process.StandardInput;
        _inputWriter.AutoFlush = true;

        // 设置控制台编码为 UTF-8
        SendCommand("chcp 65001 | Out-Null");
        // 设置提示符格式
        SendCommand("function prompt { \"PS $($executionContext.SessionState.Path.CurrentLocation)> \" }");
    }

    public void SendCommand(string command)
    {
        if (!IsRunning || _inputWriter == null) return;

        try
        {
            _inputWriter.WriteLine(command);
        }
        catch (Exception ex)
        {
            ErrorReceived?.Invoke(this, $"发送命令失败: {ex.Message}");
        }
    }

    public void SendInput(string input)
    {
        if (!IsRunning || _inputWriter == null) return;

        try
        {
            _inputWriter.Write(input);
            _inputWriter.Flush();
        }
        catch
        {
            // 忽略
        }
    }

    public void SendCtrlC()
    {
        if (!IsRunning || _process == null) return;

        try
        {
            // 发送 Ctrl+C 信号来中断当前命令
            GenerateConsoleCtrlEvent(0, (uint)_process.Id);
        }
        catch
        {
            // 如果失败，尝试发送换行
            SendInput("\n");
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null)
        {
            // 尝试提取当前目录
            if (e.Data.StartsWith("PS ") && e.Data.Contains(">"))
            {
                var path = e.Data.Substring(3, e.Data.LastIndexOf('>') - 3).Trim();
                if (Directory.Exists(path))
                {
                    CurrentDirectory = path;
                }
            }

            OutputReceived?.Invoke(this, e.Data);
        }
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null)
        {
            ErrorReceived?.Invoke(this, e.Data);
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        ProcessExited?.Invoke(this, EventArgs.Empty);
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    public void Stop()
    {
        if (_process != null && !_process.HasExited)
        {
            try
            {
                _inputWriter?.WriteLine("exit");
                _process.WaitForExit(1000);
                if (!_process.HasExited)
                {
                    _process.Kill();
                }
            }
            catch
            {
                // 忽略
            }
        }

        _inputWriter?.Dispose();
        _process?.Dispose();
        _inputWriter = null;
        _process = null;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        Stop();
        _isDisposed = true;
    }
}

