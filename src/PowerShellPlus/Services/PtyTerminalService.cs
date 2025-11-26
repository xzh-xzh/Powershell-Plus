using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PowerShellPlus.Services;

/// <summary>
/// 基于 Windows ConPTY 的终端服务 - 提供接近 Windows Terminal 的原生体验
/// </summary>
public class PtyTerminalService : IDisposable
{
    private SafeFileHandle? _pipeInRead;
    private SafeFileHandle? _pipeInWrite;
    private SafeFileHandle? _pipeOutRead;
    private SafeFileHandle? _pipeOutWrite;
    private IntPtr _pseudoConsoleHandle;
    private SafeProcessHandle? _processHandle;
    private SafeThreadHandle? _threadHandle;
    private CancellationTokenSource? _readCts;
    private FileStream? _writeStream;
    private bool _isDisposed;

    public event EventHandler<string>? OutputReceived;
    public event EventHandler? ProcessExited;

    public bool IsRunning => _processHandle != null && !_processHandle.IsInvalid && !_processHandle.IsClosed;
    public int Columns { get; private set; } = 120;
    public int Rows { get; private set; } = 30;

    /// <summary>
    /// 启动伪终端
    /// </summary>
    public async Task StartAsync()
    {
        if (IsRunning) return;

        try
        {
            // 创建管道
            CreatePipes();

            // 创建伪控制台
            var size = new COORD { X = (short)Columns, Y = (short)Rows };
            var result = CreatePseudoConsole(size, _pipeOutRead!, _pipeInWrite!, 0, out _pseudoConsoleHandle);
            if (result != 0)
            {
                throw new Win32Exception(result, $"CreatePseudoConsole 失败: {result}");
            }

            // 启动 PowerShell 进程
            StartProcess();

            // 创建持久写入流
            _writeStream = new FileStream(
                new SafeFileHandle(_pipeOutWrite!.DangerousGetHandle(), ownsHandle: false),
                FileAccess.Write, 4096, false);

            // 开始读取输出
            _readCts = new CancellationTokenSource();
            _ = ReadOutputAsync(_readCts.Token);
        }
        catch (Exception ex)
        {
            OutputReceived?.Invoke(this, $"[错误] 启动终端失败: {ex.Message}\r\n");
            OutputReceived?.Invoke(this, $"[提示] 请确保系统为 Windows 10 1809 或更高版本\r\n");
        }

        await Task.CompletedTask;
    }

    private void CreatePipes()
    {
        var securityAttributes = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = false
        };

        // 创建输入管道 (我们写入 -> 进程读取)
        if (!CreatePipe(out _pipeOutRead, out _pipeOutWrite, ref securityAttributes, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建输入管道失败");
        }

        // 创建输出管道 (进程写入 -> 我们读取)
        if (!CreatePipe(out _pipeInRead, out _pipeInWrite, ref securityAttributes, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建输出管道失败");
        }
    }

    private void StartProcess()
    {
        var startupInfo = new STARTUPINFOEX();
        startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();

        // 初始化属性列表
        IntPtr attributeListSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeListSize);

        startupInfo.lpAttributeList = Marshal.AllocHGlobal(attributeListSize);

        if (!InitializeProcThreadAttributeList(startupInfo.lpAttributeList, 1, 0, ref attributeListSize))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeProcThreadAttributeList 失败");
        }

        // 设置伪控制台属性
        if (!UpdateProcThreadAttribute(
            startupInfo.lpAttributeList,
            0,
            (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
            _pseudoConsoleHandle,
            (IntPtr)IntPtr.Size,
            IntPtr.Zero,
            IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateProcThreadAttribute 失败");
        }

        // 创建进程
        var processInfo = new PROCESS_INFORMATION();
        var commandLine = "powershell.exe -NoLogo";
        var workingDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!CreateProcess(
            null,
            commandLine,
            IntPtr.Zero,
            IntPtr.Zero,
            false,
            EXTENDED_STARTUPINFO_PRESENT,
            IntPtr.Zero,
            workingDir,
            ref startupInfo,
            out processInfo))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcess 失败");
        }

        _processHandle = new SafeProcessHandle(processInfo.hProcess, true);
        _threadHandle = new SafeThreadHandle(processInfo.hThread, true);

        // 监控进程退出
        _ = MonitorProcessExitAsync();

        // 清理属性列表
        DeleteProcThreadAttributeList(startupInfo.lpAttributeList);
        Marshal.FreeHGlobal(startupInfo.lpAttributeList);
    }

    private async Task MonitorProcessExitAsync()
    {
        if (_processHandle == null) return;

        await Task.Run(() =>
        {
            WaitForSingleObject(_processHandle.DangerousGetHandle(), INFINITE);
            ProcessExited?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>
    /// 异步读取终端输出
    /// </summary>
    private async Task ReadOutputAsync(CancellationToken cancellationToken)
    {
        if (_pipeInRead == null) return;

        var buffer = new byte[4096];

        try
        {
            using var stream = new FileStream(_pipeInRead, FileAccess.Read, 4096, false);

            while (!cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0) break;

                // 尝试 UTF-8，如果失败则使用 GBK
                string text;
                try
                {
                    text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                }
                catch
                {
                    text = Encoding.GetEncoding("gbk").GetString(buffer, 0, bytesRead);
                }

                OutputReceived?.Invoke(this, text);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (Exception ex)
        {
            OutputReceived?.Invoke(this, $"\r\n[错误] 读取输出失败: {ex.Message}\r\n");
        }
    }

    /// <summary>
    /// 发送命令到终端
    /// </summary>
    public void SendCommand(string command)
    {
        SendInput(command + "\r");
    }

    /// <summary>
    /// 发送原始输入（支持特殊按键）
    /// </summary>
    public void SendInput(string input)
    {
        if (_writeStream == null) return;

        try
        {
            var data = Encoding.UTF8.GetBytes(input);
            _writeStream.Write(data, 0, data.Length);
            _writeStream.Flush();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SendInput error: {ex.Message}");
        }
    }

    /// <summary>
    /// 发送 Ctrl+C 中断信号
    /// </summary>
    public void SendCtrlC()
    {
        SendInput("\x03");
    }

    /// <summary>
    /// 发送 Ctrl+D
    /// </summary>
    public void SendCtrlD()
    {
        SendInput("\x04");
    }

    /// <summary>
    /// 发送上箭头（历史命令）
    /// </summary>
    public void SendUpArrow()
    {
        SendInput("\x1b[A");
    }

    /// <summary>
    /// 发送下箭头
    /// </summary>
    public void SendDownArrow()
    {
        SendInput("\x1b[B");
    }

    /// <summary>
    /// 发送 Tab 键（自动补全）
    /// </summary>
    public void SendTab()
    {
        SendInput("\t");
    }

    /// <summary>
    /// 调整终端大小
    /// </summary>
    public void Resize(int columns, int rows)
    {
        Columns = columns;
        Rows = rows;

        if (_pseudoConsoleHandle != IntPtr.Zero)
        {
            var size = new COORD { X = (short)columns, Y = (short)rows };
            ResizePseudoConsole(_pseudoConsoleHandle, size);
        }
    }

    /// <summary>
    /// 重启终端
    /// </summary>
    public async Task RestartAsync()
    {
        Stop();
        await StartAsync();
    }

    /// <summary>
    /// 停止终端
    /// </summary>
    public void Stop()
    {
        _readCts?.Cancel();
        _readCts?.Dispose();
        _readCts = null;

        // 关闭写入流
        _writeStream?.Dispose();
        _writeStream = null;

        // 关闭伪控制台
        if (_pseudoConsoleHandle != IntPtr.Zero)
        {
            ClosePseudoConsole(_pseudoConsoleHandle);
            _pseudoConsoleHandle = IntPtr.Zero;
        }

        // 关闭进程句柄
        _processHandle?.Dispose();
        _processHandle = null;

        _threadHandle?.Dispose();
        _threadHandle = null;

        // 关闭管道
        _pipeInRead?.Dispose();
        _pipeInWrite?.Dispose();
        _pipeOutRead?.Dispose();
        _pipeOutWrite?.Dispose();
        _pipeInRead = null;
        _pipeInWrite = null;
        _pipeOutRead = null;
        _pipeOutWrite = null;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        Stop();
        _isDisposed = true;
    }

    #region Windows API 声明

    private const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const uint INFINITE = 0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bInheritHandle;
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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out SafeFileHandle hReadPipe,
        out SafeFileHandle hWritePipe,
        ref SECURITY_ATTRIBUTES lpPipeAttributes,
        uint nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(
        COORD size,
        SafeFileHandle hInput,
        SafeFileHandle hOutput,
        uint dwFlags,
        out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr Attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    #endregion
}

/// <summary>
/// 安全线程句柄
/// </summary>
public class SafeThreadHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeThreadHandle(IntPtr handle, bool ownsHandle) : base(ownsHandle)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        return CloseHandle(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
