using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace PowerShellPlus.Controls;

/// <summary>
/// 嵌入外部控制台窗口的 HwndHost
/// </summary>
public class EmbeddedConsoleHost : HwndHost
{
    private Process? _process;
    private IntPtr _consoleHandle = IntPtr.Zero;
    private IntPtr _hostHandle = IntPtr.Zero;
    private bool _isDisposed;
    private bool _isEmbedded;

    public event EventHandler? ProcessExited;
    public event EventHandler? ConsoleReady;

    #region Win32 API

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    private const int GWL_STYLE = -16;
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_SYSMENU = 0x00080000;
    private const int SW_SHOW = 5;

    private const uint WM_CHAR = 0x0102;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;

    private const byte VK_RETURN = 0x0D;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_C = 0x43;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    #endregion

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        // 创建一个宿主窗口作为容器
        _hostHandle = CreateWindowEx(
            0,
            "static",
            "",
            WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
            0, 0,
            (int)ActualWidth,
            (int)ActualHeight,
            hwndParent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        // 异步启动 PowerShell 并嵌入
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(async () =>
        {
            await StartAndEmbedPowerShellAsync();
        }));

        return new HandleRef(this, _hostHandle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        Dispose(true);
        if (_hostHandle != IntPtr.Zero)
        {
            DestroyWindow(_hostHandle);
            _hostHandle = IntPtr.Zero;
        }
    }

    private async Task StartAndEmbedPowerShellAsync()
    {
        try
        {
            // 启动 PowerShell 进程
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
            };

            _process = Process.Start(startInfo);

            if (_process == null) return;

            _process.EnableRaisingEvents = true;
            _process.Exited += (s, e) =>
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    ProcessExited?.Invoke(this, EventArgs.Empty);
                });
            };

            // 异步等待窗口创建
            _consoleHandle = await WaitForWindowHandleAsync(_process, TimeSpan.FromSeconds(10));

            if (_consoleHandle == IntPtr.Zero)
            {
                Debug.WriteLine("无法获取 PowerShell 窗口句柄");
                return;
            }

            // 在 UI 线程上嵌入窗口
            await Dispatcher.InvokeAsync(() =>
            {
                EmbedConsoleWindow();
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"启动 PowerShell 失败: {ex.Message}");
        }
    }

    private static async Task<IntPtr> WaitForWindowHandleAsync(Process process, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        
        while (stopwatch.Elapsed < timeout)
        {
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return process.MainWindowHandle;
            }
            await Task.Delay(100);
        }

        return IntPtr.Zero;
    }

    private void EmbedConsoleWindow()
    {
        if (_consoleHandle == IntPtr.Zero || _hostHandle == IntPtr.Zero) return;

        try
        {
            // 修改窗口样式为子窗口
            var style = GetWindowLong(_consoleHandle, GWL_STYLE);
            style = style & ~WS_CAPTION & ~WS_THICKFRAME & ~WS_MINIMIZEBOX & ~WS_MAXIMIZEBOX & ~WS_SYSMENU;
            style = style | WS_CHILD | WS_VISIBLE;
            SetWindowLong(_consoleHandle, GWL_STYLE, style);

            // 设置父窗口
            SetParent(_consoleHandle, _hostHandle);

            // 调整大小填充容器
            var width = (int)ActualWidth;
            var height = (int)ActualHeight;
            if (width > 0 && height > 0)
            {
                MoveWindow(_consoleHandle, 0, 0, width, height, true);
            }

            ShowWindow(_consoleHandle, SW_SHOW);
            _isEmbedded = true;

            ConsoleReady?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"嵌入窗口失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 调整嵌入窗口大小
    /// </summary>
    public void ResizeConsole(int width, int height)
    {
        if (_consoleHandle != IntPtr.Zero && _isEmbedded)
        {
            MoveWindow(_consoleHandle, 0, 0, width, height, true);
        }
    }

    /// <summary>
    /// 发送命令到控制台
    /// </summary>
    public void SendCommand(string command)
    {
        if (_consoleHandle == IntPtr.Zero || !_isEmbedded) return;

        Task.Run(() =>
        {
            try
            {
                // 发送每个字符
                foreach (char c in command)
                {
                    PostMessage(_consoleHandle, WM_CHAR, (IntPtr)c, IntPtr.Zero);
                    Thread.Sleep(5);
                }

                // 发送回车
                Thread.Sleep(10);
                PostMessage(_consoleHandle, WM_KEYDOWN, (IntPtr)VK_RETURN, IntPtr.Zero);
                PostMessage(_consoleHandle, WM_KEYUP, (IntPtr)VK_RETURN, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"发送命令失败: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 发送 Ctrl+C 中断
    /// </summary>
    public void SendCtrlC()
    {
        if (_consoleHandle == IntPtr.Zero || !_isEmbedded) return;

        Task.Run(() =>
        {
            try
            {
                SetForegroundWindow(_consoleHandle);
                Thread.Sleep(50);

                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                keybd_event(VK_C, 0, 0, UIntPtr.Zero);
                keybd_event(VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"发送 Ctrl+C 失败: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 聚焦到控制台
    /// </summary>
    public void FocusConsole()
    {
        if (_consoleHandle != IntPtr.Zero && _isEmbedded)
        {
            SetForegroundWindow(_consoleHandle);
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        
        if (_consoleHandle != IntPtr.Zero && _isEmbedded)
        {
            ResizeConsole((int)sizeInfo.NewSize.Width, (int)sizeInfo.NewSize.Height);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed) return;

        if (disposing)
        {
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill();
                    _process.Dispose();
                }
            }
            catch { }
        }

        _consoleHandle = IntPtr.Zero;
        _isEmbedded = false;
        _isDisposed = true;

        base.Dispose(disposing);
    }
}
