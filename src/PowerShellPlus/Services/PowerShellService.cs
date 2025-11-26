using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;

namespace PowerShellPlus.Services;

public class PowerShellService : IDisposable
{
    private Runspace? _runspace;
    private string _currentDirectory;
    private bool _isDisposed;

    public event EventHandler<string>? OutputReceived;
    public event EventHandler<string>? ErrorReceived;
    public event EventHandler<string>? DirectoryChanged;
    public event EventHandler? CommandCompleted;
    public event EventHandler? ClearRequested;  // 新增：清屏请求事件

    public string CurrentDirectory => _currentDirectory;
    public bool IsBusy { get; private set; }

    // 需要特殊处理的命令（这些命令在非交互式宿主中会出问题）
    private static readonly HashSet<string> ClearCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "cls", "clear", "clear-host"
    };

    // 不支持的交互式命令
    private static readonly HashSet<string> UnsupportedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "read-host", "pause"
    };

    public PowerShellService()
    {
        _currentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        InitializeRunspace();
    }

    private void InitializeRunspace()
    {
        var initialSessionState = InitialSessionState.CreateDefault();
        _runspace = RunspaceFactory.CreateRunspace(initialSessionState);
        _runspace.Open();

        // 设置初始目录
        using var ps = System.Management.Automation.PowerShell.Create();
        ps.Runspace = _runspace;
        ps.AddCommand("Set-Location").AddParameter("Path", _currentDirectory);
        ps.Invoke();
    }

    public async Task<string> ExecuteCommandAsync(string command)
    {
        if (IsBusy)
        {
            return "# 当前有命令正在执行，请等待完成";
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        var trimmedCommand = command.Trim();

        // 检查是否是清屏命令
        if (IsClearCommand(trimmedCommand))
        {
            ClearRequested?.Invoke(this, EventArgs.Empty);
            CommandCompleted?.Invoke(this, EventArgs.Empty);
            return string.Empty;
        }

        // 检查是否包含不支持的命令
        if (ContainsUnsupportedCommand(trimmedCommand))
        {
            var msg = "# 此命令包含不支持的交互式操作（如 Read-Host、Pause）";
            ErrorReceived?.Invoke(this, msg);
            CommandCompleted?.Invoke(this, EventArgs.Empty);
            return msg;
        }

        IsBusy = true;
        var output = new StringBuilder();

        try
        {
            await Task.Run(() =>
            {
                using var ps = System.Management.Automation.PowerShell.Create();
                ps.Runspace = _runspace;

                // 显示命令提示符和命令
                var prompt = $"PS {_currentDirectory}> {command}";
                output.AppendLine(prompt);
                OutputReceived?.Invoke(this, prompt);

                ps.AddScript(command);

                // 收集输出
                var results = ps.Invoke();

                foreach (var result in results)
                {
                    var line = result?.ToString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(line))
                    {
                        output.AppendLine(line);
                        OutputReceived?.Invoke(this, line);
                    }
                }

                // 处理错误
                if (ps.Streams.Error.Count > 0)
                {
                    foreach (var error in ps.Streams.Error)
                    {
                        var errorLine = error.ToString();
                        output.AppendLine(errorLine);
                        ErrorReceived?.Invoke(this, errorLine);
                    }
                }

                // 处理警告
                if (ps.Streams.Warning.Count > 0)
                {
                    foreach (var warning in ps.Streams.Warning)
                    {
                        var warningLine = $"[警告] {warning}";
                        output.AppendLine(warningLine);
                        OutputReceived?.Invoke(this, warningLine);
                    }
                }

                // 更新当前目录
                ps.Commands.Clear();
                ps.AddCommand("Get-Location");
                var locationResults = ps.Invoke();
                if (locationResults.Count > 0)
                {
                    var newPath = locationResults[0].BaseObject.ToString() ?? _currentDirectory;
                    if (newPath != _currentDirectory)
                    {
                        _currentDirectory = newPath;
                        DirectoryChanged?.Invoke(this, _currentDirectory);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            var errorMsg = $"执行错误: {ex.Message}";
            output.AppendLine(errorMsg);
            ErrorReceived?.Invoke(this, errorMsg);
        }
        finally
        {
            IsBusy = false;
            CommandCompleted?.Invoke(this, EventArgs.Empty);
        }

        return output.ToString();
    }

    /// <summary>
    /// 检查是否是清屏命令
    /// </summary>
    private bool IsClearCommand(string command)
    {
        // 直接匹配或者命令以这些开头
        var firstWord = command.Split(' ', ';', '|')[0].Trim();
        return ClearCommands.Contains(firstWord);
    }

    /// <summary>
    /// 检查是否包含不支持的交互式命令
    /// </summary>
    private bool ContainsUnsupportedCommand(string command)
    {
        var lowerCommand = command.ToLowerInvariant();
        foreach (var unsupported in UnsupportedCommands)
        {
            if (lowerCommand.Contains(unsupported.ToLowerInvariant()))
            {
                return true;
            }
        }
        return false;
    }

    public string GetPrompt()
    {
        return $"PS {_currentDirectory}>";
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        
        _runspace?.Close();
        _runspace?.Dispose();
        _isDisposed = true;
    }
}
