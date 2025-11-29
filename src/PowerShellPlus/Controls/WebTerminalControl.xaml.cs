using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using PowerShellPlus.Services;

namespace PowerShellPlus.Controls;

/// <summary>
/// 基于 WebView2 + xterm.js 的终端控件
/// </summary>
public partial class WebTerminalControl : UserControl, IDisposable
{
    private readonly ConPtyService _ptyService;
    private bool _isInitialized;
    private bool _isDisposed;
    private readonly StringBuilder _outputBuffer = new();
    private readonly System.Timers.Timer _flushTimer;
    private readonly object _bufferLock = new();
    
    // 用于上下文跟踪
    private readonly StringBuilder _recentOutputBuffer = new();
    private readonly object _recentOutputLock = new();
    private const int MaxRecentOutputLines = 50;
    private string _currentTitle = "PowerShell";
    private string? _lastCommand;

    /// <summary>
    /// 终端就绪事件
    /// </summary>
    public event EventHandler? TerminalReady;

    /// <summary>
    /// 终端标题变更事件
    /// </summary>
    public event EventHandler<string>? TitleChanged;

    /// <summary>
    /// 进程退出事件
    /// </summary>
    public event EventHandler<int>? ProcessExited;

    /// <summary>
    /// 终端是否就绪
    /// </summary>
    public bool IsReady => _isInitialized && _ptyService.IsRunning;

    /// <summary>
    /// 当前终端标题（通常包含当前目录）
    /// </summary>
    public string CurrentTitle => _currentTitle;

    /// <summary>
    /// 最后执行的命令
    /// </summary>
    public string? LastCommand => _lastCommand;

    /// <summary>
    /// 获取最近的终端输出
    /// </summary>
    public string GetRecentOutput(int maxLines = 30)
    {
        lock (_recentOutputLock)
        {
            var content = _recentOutputBuffer.ToString();
            if (string.IsNullOrEmpty(content))
                return string.Empty;

            // 分割成行并取最后N行
            var lines = content.Split('\n');
            var startIndex = Math.Max(0, lines.Length - maxLines);
            var recentLines = lines.Skip(startIndex).ToArray();
            
            return string.Join("\n", recentLines).Trim();
        }
    }

    /// <summary>
    /// 从标题中提取当前工作目录
    /// PowerShell 标题格式通常为: "管理员: Windows PowerShell" 或包含路径信息
    /// </summary>
    public string GetCurrentDirectory()
    {
        // PowerShell 的标题可能包含路径，尝试提取
        // 常见格式: "C:\Users\xxx - PowerShell" 或 "PowerShell - C:\Users\xxx"
        var title = _currentTitle;
        
        // 尝试匹配路径模式
        var pathPattern = @"([A-Za-z]:\\[^\r\n\*\?""<>\|]*?)(?:\s*[-–—]|\s*$)";
        var match = System.Text.RegularExpressions.Regex.Match(title, pathPattern);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }
        
        // 如果标题不包含路径，返回用户目录作为默认值
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>
    /// 清除最近输出缓冲区
    /// </summary>
    public void ClearRecentOutputBuffer()
    {
        lock (_recentOutputLock)
        {
            _recentOutputBuffer.Clear();
        }
    }

    public WebTerminalControl()
    {
        InitializeComponent();

        _ptyService = new ConPtyService();
        _ptyService.OutputReceived += OnPtyOutputReceived;
        _ptyService.ProcessExited += OnPtyProcessExited;

        // 设置输出缓冲刷新定时器（提高性能）
        _flushTimer = new System.Timers.Timer(16); // ~60fps
        _flushTimer.Elapsed += FlushOutputBuffer;
        _flushTimer.AutoReset = true;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await InitializeAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Dispose();
    }

    /// <summary>
    /// 初始化终端
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        try
        {
            ShowLoading();

            // 初始化 WebView2
            var env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(Path.GetTempPath(), "PowerShellPlus_WebView2"));

            await TerminalWebView.EnsureCoreWebView2Async(env);

            // 配置 WebView2
            ConfigureWebView();

            // 加载终端 HTML
            await LoadTerminalHtmlAsync();

            Debug.WriteLine("WebView2 initialized successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to initialize WebView2: {ex.Message}");
            ShowError($"终端初始化失败: {ex.Message}");
        }
    }

    private void ConfigureWebView()
    {
        var settings = TerminalWebView.CoreWebView2.Settings;

        // 禁用不需要的功能
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreDevToolsEnabled = true; // 调试时可以开启
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;

        // 启用需要的功能
        settings.IsScriptEnabled = true;
        settings.IsWebMessageEnabled = true;

        // 监听来自 JavaScript 的消息
        TerminalWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        // 监听导航完成
        TerminalWebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
    }

    private async Task LoadTerminalHtmlAsync()
    {
        // 尝试从资源文件加载
        var htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Terminal", "terminal.html");

        if (File.Exists(htmlPath))
        {
            TerminalWebView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
        }
        else
        {
            // 如果文件不存在，使用内嵌的 HTML（从 CDN 加载 xterm.js）
            Debug.WriteLine($"Terminal HTML not found at: {htmlPath}, using embedded HTML");
            var html = GetEmbeddedTerminalHtml();
            TerminalWebView.CoreWebView2.NavigateToString(html);
        }

        await Task.CompletedTask;
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            Debug.WriteLine("Terminal HTML loaded successfully");
        }
        else
        {
            Debug.WriteLine($"Navigation failed: {e.WebErrorStatus}");
            ShowError($"加载终端页面失败: {e.WebErrorStatus}");
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = e.WebMessageAsJson;
            var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "ready":
                    await OnTerminalReadyAsync(root);
                    break;

                case "input":
                    var inputData = root.GetProperty("data").GetString();
                    if (!string.IsNullOrEmpty(inputData))
                    {
                        _ptyService.Write(inputData);
                    }
                    break;

                case "resize":
                    var cols = root.GetProperty("cols").GetInt32();
                    var rows = root.GetProperty("rows").GetInt32();
                    _ptyService.Resize(cols, rows);
                    break;

                case "title":
                    var title = root.GetProperty("title").GetString();
                    if (!string.IsNullOrEmpty(title))
                    {
                        _currentTitle = title;
                        TitleChanged?.Invoke(this, title);
                    }
                    break;

                case "binary":
                    var binaryData = root.GetProperty("data").GetString();
                    if (!string.IsNullOrEmpty(binaryData))
                    {
                        var bytes = Encoding.UTF8.GetBytes(binaryData);
                        _ptyService.WriteBytes(bytes);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error processing web message: {ex.Message}");
        }
    }

    private async Task OnTerminalReadyAsync(JsonElement root)
    {
        var cols = root.GetProperty("cols").GetInt32();
        var rows = root.GetProperty("rows").GetInt32();

        Debug.WriteLine($"Terminal ready: cols={cols}, rows={rows}");

        // 启动 PTY
        try
        {
            await _ptyService.StartAsync(cols, rows);
            _isInitialized = true;

            // 启动输出缓冲刷新
            _flushTimer.Start();

            // 隐藏加载提示
            Dispatcher.Invoke(() =>
            {
                HideLoading();
                TerminalReady?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to start PTY: {ex.Message}");
            Dispatcher.Invoke(() => ShowError($"启动终端失败: {ex.Message}"));
        }
    }

    private void OnPtyOutputReceived(byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);
        
        // 将输出添加到缓冲区（提高性能）
        lock (_bufferLock)
        {
            _outputBuffer.Append(text);
        }
        
        // 同时保存到最近输出缓冲区（用于AI上下文）
        lock (_recentOutputLock)
        {
            _recentOutputBuffer.Append(text);
            // 限制缓冲区大小
            if (_recentOutputBuffer.Length > 10000)
            {
                var content = _recentOutputBuffer.ToString();
                _recentOutputBuffer.Clear();
                // 保留后半部分
                _recentOutputBuffer.Append(content.Substring(content.Length - 5000));
            }
        }
    }

    private void FlushOutputBuffer(object? sender, System.Timers.ElapsedEventArgs e)
    {
        string? output = null;

        lock (_bufferLock)
        {
            if (_outputBuffer.Length > 0)
            {
                output = _outputBuffer.ToString();
                _outputBuffer.Clear();
            }
        }

        if (!string.IsNullOrEmpty(output))
        {
            Dispatcher.BeginInvoke(() => WriteToTerminal(output));
        }
    }

    private void OnPtyProcessExited(int exitCode)
    {
        Debug.WriteLine($"PTY process exited with code: {exitCode}");
        _flushTimer.Stop();

        Dispatcher.Invoke(() =>
        {
            ProcessExited?.Invoke(this, exitCode);
        });
    }

    /// <summary>
    /// 向终端写入数据
    /// </summary>
    private async void WriteToTerminal(string data)
    {
        if (!_isInitialized || TerminalWebView.CoreWebView2 == null) return;

        try
        {
            // 使用 Base64 编码传输以处理特殊字符
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(data));
            await TerminalWebView.CoreWebView2.ExecuteScriptAsync($"writeBase64ToTerminal('{base64}')");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error writing to terminal: {ex.Message}");
        }
    }

    /// <summary>
    /// 发送命令到终端
    /// </summary>
    public void SendCommand(string command)
    {
        _lastCommand = command;
        _ptyService.SendCommand(command);
    }

    /// <summary>
    /// 发送 Ctrl+C
    /// </summary>
    public void SendCtrlC()
    {
        _ptyService.SendCtrlC();
    }

    /// <summary>
    /// 清屏
    /// </summary>
    public async void Clear()
    {
        if (!_isInitialized || TerminalWebView.CoreWebView2 == null) return;

        try
        {
            await TerminalWebView.CoreWebView2.ExecuteScriptAsync("clearTerminal()");
            // 同时发送 clear 命令给 PowerShell
            _ptyService.Write("cls\r");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error clearing terminal: {ex.Message}");
        }
    }

    /// <summary>
    /// 聚焦终端
    /// </summary>
    public async void FocusTerminal()
    {
        if (!_isInitialized || TerminalWebView.CoreWebView2 == null) return;

        try
        {
            TerminalWebView.Focus();
            await TerminalWebView.CoreWebView2.ExecuteScriptAsync("focusTerminal()");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error focusing terminal: {ex.Message}");
        }
    }

    /// <summary>
    /// 设置字体大小
    /// </summary>
    public async void SetFontSize(int size)
    {
        if (!_isInitialized || TerminalWebView.CoreWebView2 == null) return;

        try
        {
            await TerminalWebView.CoreWebView2.ExecuteScriptAsync($"setFontSize({size})");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error setting font size: {ex.Message}");
        }
    }

    private void ShowLoading()
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        ErrorOverlay.Visibility = Visibility.Collapsed;
    }

    private void HideLoading()
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;
        ErrorOverlay.Visibility = Visibility.Visible;
        ErrorMessage.Text = message;
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        _isInitialized = false;
        await InitializeAsync();
    }

    private static string GetEmbeddedTerminalHtml()
    {
        return """
            <!DOCTYPE html>
            <html lang="zh-CN">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>PowerShell Terminal</title>
                <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/@xterm/xterm@5.5.0/css/xterm.css" />
                <style>
                    * { margin: 0; padding: 0; box-sizing: border-box; }
                    html, body { width: 100%; height: 100%; overflow: hidden; background-color: #0c0c0c; }
                    #terminal { width: 100%; height: 100%; }
                    .xterm-viewport::-webkit-scrollbar { width: 10px; }
                    .xterm-viewport::-webkit-scrollbar-track { background: #1a1a1a; }
                    .xterm-viewport::-webkit-scrollbar-thumb { background: #3a3a3a; border-radius: 5px; }
                    .xterm-viewport::-webkit-scrollbar-thumb:hover { background: #4a4a4a; }
                </style>
            </head>
            <body>
                <div id="terminal"></div>
                <script src="https://cdn.jsdelivr.net/npm/@xterm/xterm@5.5.0/lib/xterm.min.js"></script>
                <script src="https://cdn.jsdelivr.net/npm/@xterm/addon-fit@0.10.0/lib/addon-fit.min.js"></script>
                <script src="https://cdn.jsdelivr.net/npm/@xterm/addon-web-links@0.11.0/lib/addon-web-links.min.js"></script>
                <script src="https://cdn.jsdelivr.net/npm/@xterm/addon-unicode11@0.8.0/lib/addon-unicode11.min.js"></script>
                <script>
                    const terminalConfig = {
                        cursorBlink: true, cursorStyle: 'block', fontSize: 14,
                        fontFamily: '"Cascadia Code", "Cascadia Mono", Consolas, "Courier New", monospace',
                        lineHeight: 1.2, letterSpacing: 0,
                        theme: {
                            background: '#0c0c0c', foreground: '#cccccc', cursor: '#ffffff',
                            cursorAccent: '#0c0c0c', selectionBackground: '#264f78',
                            selectionForeground: '#ffffff', selectionInactiveBackground: '#3a3d41',
                            black: '#0c0c0c', red: '#c50f1f', green: '#13a10e', yellow: '#c19c00',
                            blue: '#0037da', magenta: '#881798', cyan: '#3a96dd', white: '#cccccc',
                            brightBlack: '#767676', brightRed: '#e74856', brightGreen: '#16c60c',
                            brightYellow: '#f9f1a5', brightBlue: '#3b78ff', brightMagenta: '#b4009e',
                            brightCyan: '#61d6d6', brightWhite: '#f2f2f2'
                        },
                        allowProposedApi: true, scrollback: 10000, tabStopWidth: 4, windowsMode: true
                    };
                    const terminal = new Terminal(terminalConfig);
                    const fitAddon = new FitAddon.FitAddon();
                    const webLinksAddon = new WebLinksAddon.WebLinksAddon();
                    const unicode11Addon = new Unicode11Addon.Unicode11Addon();
                    terminal.loadAddon(fitAddon);
                    terminal.loadAddon(webLinksAddon);
                    terminal.loadAddon(unicode11Addon);
                    terminal.unicode.activeVersion = '11';
                    const container = document.getElementById('terminal');
                    terminal.open(container);
                    setTimeout(() => { fitAddon.fit(); notifySize(); }, 100);
                    let resizeTimeout;
                    window.addEventListener('resize', () => {
                        clearTimeout(resizeTimeout);
                        resizeTimeout = setTimeout(() => { fitAddon.fit(); notifySize(); }, 50);
                    });
                    function notifySize() {
                        if (window.chrome && window.chrome.webview) {
                            window.chrome.webview.postMessage({ type: 'resize', cols: terminal.cols, rows: terminal.rows });
                        }
                    }
                    terminal.onData(data => {
                        if (window.chrome && window.chrome.webview) {
                            window.chrome.webview.postMessage({ type: 'input', data: data });
                        }
                    });
                    terminal.onTitleChange(title => {
                        if (window.chrome && window.chrome.webview) {
                            window.chrome.webview.postMessage({ type: 'title', title: title });
                        }
                    });
                    function writeToTerminal(data) { terminal.write(data); }
                    function writeBase64ToTerminal(base64Data) { const b = atob(base64Data), u = new Uint8Array(b.length); for(let i=0; i<b.length; i++) u[i] = b.charCodeAt(i); terminal.write(new TextDecoder('utf-8').decode(u)); }
                    function clearTerminal() { terminal.clear(); }
                    function resetTerminal() { terminal.reset(); }
                    function focusTerminal() { terminal.focus(); }
                    function getTerminalSize() { return { cols: terminal.cols, rows: terminal.rows }; }
                    function setFontSize(size) { terminal.options.fontSize = size; fitAddon.fit(); notifySize(); }
                    function scrollToBottom() { terminal.scrollToBottom(); }
                    if (window.chrome && window.chrome.webview) {
                        window.chrome.webview.postMessage({ type: 'ready', cols: terminal.cols, rows: terminal.rows });
                    }
                    terminal.focus();
                    container.addEventListener('click', () => { terminal.focus(); });
                </script>
            </body>
            </html>
            """;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _flushTimer.Stop();
        _flushTimer.Dispose();

        _ptyService.OutputReceived -= OnPtyOutputReceived;
        _ptyService.ProcessExited -= OnPtyProcessExited;
        _ptyService.Dispose();

        GC.SuppressFinalize(this);
    }
}



