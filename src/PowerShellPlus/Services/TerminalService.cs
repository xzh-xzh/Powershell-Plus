using System.Diagnostics;
using System.IO;
using System.Text;

namespace PowerShellPlus.Services;

/// <summary>
/// PowerShell 终端服务 - 交互模式
/// </summary>
public class TerminalService : IDisposable
{
    private Process? _process;
    private StreamWriter? _inputWriter;
    private bool _isDisposed;

    public event EventHandler<string>? OutputReceived;
    public event EventHandler<string>? ErrorReceived;
    public event EventHandler? ProcessExited;
    public event EventHandler<string>? DirectoryChanged;

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
            // 交互模式：不使用 -Command，让 PowerShell 正常运行并输出提示符
            Arguments = "-NoLogo -NoExit",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.GetEncoding("gbk"), // Windows 默认编码
            StandardErrorEncoding = Encoding.GetEncoding("gbk"),
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

    public void SendCtrlC()
    {
        if (!IsRunning || _process == null) return;

        try
        {
            // 尝试中断 - 发送空行
            _inputWriter?.WriteLine();
        }
        catch
        {
            // 忽略
        }
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data == null) return;

        // 尝试从提示符中提取当前目录
        // PowerShell 提示符格式: "PS C:\path> " 
        var line = e.Data;
        if (line.StartsWith("PS ") && line.TrimEnd().EndsWith(">"))
        {
            var endIndex = line.LastIndexOf('>');
            if (endIndex > 3)
            {
                var path = line.Substring(3, endIndex - 3).Trim();
                if (Directory.Exists(path) && path != CurrentDirectory)
                {
                    CurrentDirectory = path;
                    DirectoryChanged?.Invoke(this, path);
                }
            }
        }

        OutputReceived?.Invoke(this, e.Data);
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
