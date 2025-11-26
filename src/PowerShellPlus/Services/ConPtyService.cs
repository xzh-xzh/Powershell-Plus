using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PowerShellPlus.Services;

/// <summary>
/// 基于 Windows ConPTY 的伪终端服务
/// 直接使用 Windows API 提供原生 PowerShell 终端体验
/// </summary>
public class ConPtyService : IDisposable
{
    private IntPtr _hPC = IntPtr.Zero;
    private SafeFileHandle? _hPipeIn;  // 我们写入这个
    private SafeFileHandle? _hPipeOut; // 我们从这个读取
    private FileStream? _writeStream;
    private FileStream? _readStream;
    private IntPtr _hProcess = IntPtr.Zero;
    private IntPtr _hThread = IntPtr.Zero;
    private int _processId;
    private Thread? _readThread;
    private Thread? _monitorThread;
    private volatile bool _isRunning;
    private bool _isDisposed;
    private int _cols = 120;
    private int _rows = 30;

    /// <summary>
    /// 接收到终端输出时触发
    /// </summary>
    public event Action<byte[]>? OutputReceived;

    /// <summary>
    /// 进程退出时触发
    /// </summary>
    public event Action<int>? ProcessExited;

    /// <summary>
    /// 终端是否正在运行
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// 当前列数
    /// </summary>
    public int Cols => _cols;

    /// <summary>
    /// 当前行数
    /// </summary>
    public int Rows => _rows;

    #region Windows API

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe, ref SECURITY_ATTRIBUTES lpPipeAttributes, int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(COORD size, IntPtr hInput, IntPtr hOutput, uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr Attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
    private const uint WAIT_OBJECT_0 = 0x00000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }

    #endregion

    /// <summary>
    /// 启动 PowerShell 伪终端
    /// </summary>
    public Task StartAsync(int cols = 120, int rows = 30, string? workingDirectory = null)
    {
        if (_isRunning)
        {
            StopAsync().Wait();
        }

        _cols = cols;
        _rows = rows;

        try
        {
            // 创建管道
            var sa = new SECURITY_ATTRIBUTES
            {
                nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
                bInheritHandle = true,
                lpSecurityDescriptor = IntPtr.Zero
            };

            // 输入管道：我们写入 hPipeInWrite -> ConPTY 读取 hPipeInRead
            if (!CreatePipe(out IntPtr hPipeInRead, out IntPtr hPipeInWrite, ref sa, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create input pipe");
            }

            // 输出管道：ConPTY 写入 hPipeOutWrite -> 我们读取 hPipeOutRead
            if (!CreatePipe(out IntPtr hPipeOutRead, out IntPtr hPipeOutWrite, ref sa, 0))
            {
                CloseHandle(hPipeInRead);
                CloseHandle(hPipeInWrite);
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create output pipe");
            }

            // 创建伪控制台
            var size = new COORD { X = (short)cols, Y = (short)rows };
            int hr = CreatePseudoConsole(size, hPipeInRead, hPipeOutWrite, 0, out _hPC);
            
            // 关闭传给 ConPTY 的管道端（ConPTY 已经复制了句柄）
            CloseHandle(hPipeInRead);
            CloseHandle(hPipeOutWrite);

            if (hr != 0)
            {
                CloseHandle(hPipeInWrite);
                CloseHandle(hPipeOutRead);
                throw new Win32Exception(hr, $"Failed to create pseudo console: HRESULT=0x{hr:X8}");
            }

            Debug.WriteLine($"ConPTY created: handle=0x{_hPC:X}");

            // 保存我们需要的管道端
            _hPipeIn = new SafeFileHandle(hPipeInWrite, true);
            _hPipeOut = new SafeFileHandle(hPipeOutRead, true);

            // 创建持久的流对象
            _writeStream = new FileStream(_hPipeIn, FileAccess.Write, 4096, false);
            _readStream = new FileStream(_hPipeOut, FileAccess.Read, 4096, false);

            // 启动 PowerShell 进程
            StartProcess(workingDirectory);

            _isRunning = true;

            // 开始读取输出
            _readThread = new Thread(ReadOutputLoop) { IsBackground = true, Name = "ConPTY-Reader" };
            _readThread.Start();

            // 开始监控进程
            _monitorThread = new Thread(MonitorProcess) { IsBackground = true, Name = "ConPTY-Monitor" };
            _monitorThread.Start();

            Debug.WriteLine($"ConPTY started: PID={_processId}, Cols={cols}, Rows={rows}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to start ConPTY: {ex.Message}");
            Cleanup();
            throw;
        }

        return Task.CompletedTask;
    }

    private void StartProcess(string? workingDirectory)
    {
        var powershellPath = FindPowerShellPath();
        
        // 对于 Windows PowerShell，需要设置 UTF-8 编码
        string args;
        if (powershellPath.Contains("WindowsPowerShell", StringComparison.OrdinalIgnoreCase))
        {
            // Windows PowerShell: 使用 -Command 来先设置编码
            args = "-NoExit -Command \"[Console]::OutputEncoding = [Console]::InputEncoding = [System.Text.Encoding]::UTF8; $OutputEncoding = [System.Text.Encoding]::UTF8\"";
        }
        else
        {
            // PowerShell 7+: 原生支持 UTF-8
            args = "";
        }
        
        var commandLine = new StringBuilder($"\"{powershellPath}\" {args}".Trim());

        Debug.WriteLine($"Starting: {commandLine}");

        // 获取属性列表大小
        IntPtr lpSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref lpSize);

        if (lpSize == IntPtr.Zero)
        {
            throw new Win32Exception("Failed to get attribute list size");
        }

        var attributeList = Marshal.AllocHGlobal(lpSize);
        bool attrListInitialized = false;

        try
        {
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref lpSize))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to initialize attribute list");
            }
            attrListInitialized = true;

            // 将伪控制台关联到进程
            if (!UpdateProcThreadAttribute(
                attributeList,
                0,
                (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                _hPC,
                (IntPtr)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to set pseudo console attribute");
            }

            var startupInfo = new STARTUPINFOEX();
            startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            startupInfo.lpAttributeList = attributeList;

            var cwd = workingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // 创建进程
            if (!CreateProcessW(
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                EXTENDED_STARTUPINFO_PRESENT,
                IntPtr.Zero,
                cwd,
                ref startupInfo,
                out var processInfo))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create process");
            }

            _hProcess = processInfo.hProcess;
            _hThread = processInfo.hThread;
            _processId = processInfo.dwProcessId;

            Debug.WriteLine($"Process started: PID={_processId}");
        }
        finally
        {
            if (attrListInitialized)
            {
                DeleteProcThreadAttributeList(attributeList);
            }
            Marshal.FreeHGlobal(attributeList);
        }
    }

    private void ReadOutputLoop()
    {
        var buffer = new byte[8192];

        try
        {
            while (_isRunning && _readStream != null && _readStream.CanRead)
            {
                try
                {
                    var bytesRead = _readStream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        var data = new byte[bytesRead];
                        Buffer.BlockCopy(buffer, 0, data, 0, bytesRead);
                        OutputReceived?.Invoke(data);
                    }
                    else
                    {
                        Debug.WriteLine("Read returned 0, stream ended");
                        break;
                    }
                }
                catch (IOException ex)
                {
                    if (_isRunning)
                    {
                        Debug.WriteLine($"Read IO exception: {ex.Message}");
                    }
                    break;
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // 正常关闭
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Read loop exception: {ex.Message}");
        }

        Debug.WriteLine("Read loop ended");
    }

    private void MonitorProcess()
    {
        try
        {
            while (_isRunning && _hProcess != IntPtr.Zero)
            {
                var result = WaitForSingleObject(_hProcess, 500);
                if (result == WAIT_OBJECT_0)
                {
                    GetExitCodeProcess(_hProcess, out uint exitCode);
                    Debug.WriteLine($"Process exited: code={exitCode}");
                    
                    _isRunning = false;
                    ProcessExited?.Invoke((int)exitCode);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Monitor exception: {ex.Message}");
        }

        Debug.WriteLine("Monitor loop ended");
    }

    /// <summary>
    /// 停止伪终端
    /// </summary>
    public Task StopAsync()
    {
        _isRunning = false;
        Cleanup();
        return Task.CompletedTask;
    }

    private void Cleanup()
    {
        _isRunning = false;

        // 先关闭伪控制台
        if (_hPC != IntPtr.Zero)
        {
            ClosePseudoConsole(_hPC);
            _hPC = IntPtr.Zero;
        }

        // 关闭流
        try { _writeStream?.Close(); } catch { }
        try { _readStream?.Close(); } catch { }
        _writeStream = null;
        _readStream = null;

        // 关闭管道句柄
        try { _hPipeIn?.Close(); } catch { }
        try { _hPipeOut?.Close(); } catch { }
        _hPipeIn = null;
        _hPipeOut = null;

        // 关闭进程句柄
        if (_hThread != IntPtr.Zero)
        {
            CloseHandle(_hThread);
            _hThread = IntPtr.Zero;
        }
        if (_hProcess != IntPtr.Zero)
        {
            CloseHandle(_hProcess);
            _hProcess = IntPtr.Zero;
        }

        _processId = 0;
    }

    /// <summary>
    /// 向终端发送输入
    /// </summary>
    public void Write(string data)
    {
        if (_writeStream == null || !_isRunning || string.IsNullOrEmpty(data)) return;

        try
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            _writeStream.Write(bytes, 0, bytes.Length);
            _writeStream.Flush();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Write error: {ex.Message}");
        }
    }

    /// <summary>
    /// 向终端发送原始字节
    /// </summary>
    public void WriteBytes(byte[] data)
    {
        if (_writeStream == null || !_isRunning || data == null || data.Length == 0) return;

        try
        {
            _writeStream.Write(data, 0, data.Length);
            _writeStream.Flush();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WriteBytes error: {ex.Message}");
        }
    }

    /// <summary>
    /// 调整终端大小
    /// </summary>
    public void Resize(int cols, int rows)
    {
        if (_hPC == IntPtr.Zero || cols <= 0 || rows <= 0) return;

        try
        {
            _cols = cols;
            _rows = rows;
            var size = new COORD { X = (short)cols, Y = (short)rows };
            ResizePseudoConsole(_hPC, size);
            Debug.WriteLine($"Resized: cols={cols}, rows={rows}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Resize error: {ex.Message}");
        }
    }

    /// <summary>
    /// 发送命令（自动添加回车）
    /// </summary>
    public void SendCommand(string command)
    {
        Write(command + "\r");
    }

    /// <summary>
    /// 发送 Ctrl+C
    /// </summary>
    public void SendCtrlC()
    {
        Write("\x03");
    }

    private static string FindPowerShellPath()
    {
        // 优先使用 PowerShell 7+（原生支持 UTF-8）
        var pwshPaths = new[]
        {
            @"C:\Program Files\PowerShell\7\pwsh.exe",
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\PowerShell\7\pwsh.exe"),
        };

        foreach (var path in pwshPaths)
        {
            if (File.Exists(path))
            {
                Debug.WriteLine($"Using PowerShell 7: {path}");
                return path;
            }
        }

        // 查找 PATH 中的 pwsh
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            try
            {
                var pwshPath = Path.Combine(dir, "pwsh.exe");
                if (File.Exists(pwshPath))
                {
                    Debug.WriteLine($"Using pwsh from PATH: {pwshPath}");
                    return pwshPath;
                }
            }
            catch { }
        }

        // 回退到 Windows PowerShell
        Debug.WriteLine("Using Windows PowerShell");
        return @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
    }

    private static IntPtr CreateEnvironmentBlock()
    {
        // 创建环境变量块，设置 UTF-8 相关变量
        var env = new Dictionary<string, string>();
        
        // 复制当前环境变量
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                env[key] = value;
            }
        }

        // 添加/覆盖 UTF-8 相关设置
        env["PYTHONIOENCODING"] = "utf-8";
        env["LANG"] = "en_US.UTF-8";
        
        // 构建环境块（Unicode 格式：key=value\0key=value\0\0）
        var sb = new StringBuilder();
        foreach (var kvp in env)
        {
            sb.Append(kvp.Key);
            sb.Append('=');
            sb.Append(kvp.Value);
            sb.Append('\0');
        }
        sb.Append('\0');

        var envBlock = Marshal.StringToHGlobalUni(sb.ToString());
        return envBlock;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _isRunning = false;
        Cleanup();

        GC.SuppressFinalize(this);
    }
}
