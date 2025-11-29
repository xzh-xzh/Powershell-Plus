using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerShellPlus.Models;

namespace PowerShellPlus.Services;

public class OpenAIService : IAIService
{
    private readonly HttpClient _httpClient;
    private AppSettings _settings;

    private const string SystemPrompt = """
        你是一个专业的 PowerShell 助手，运行在一个 AI 增强的 PowerShell 终端应用中。
        你既可以进行对话、回答问题，也可以帮助用户生成和执行 PowerShell 命令。

        ## 你的能力：
        1. **对话与解答**：回答关于 PowerShell、系统管理、编程等问题
        2. **命令生成**：根据用户描述生成 PowerShell 命令
        3. **命令解释**：解释命令的作用和参数含义
        4. **错误分析**：分析终端输出中的错误并给出解决方案

        ## 响应格式规则：
        - 如果用户明确需要执行某个操作或生成命令，在回复中使用 ```powershell 代码块包裹命令
        - 如果只是普通对话或解释，直接用自然语言回复
        - 可以在同一个回复中既解释又提供命令
        - 命令应当简洁、安全、高效

        ## 安全规则：
        - 不要执行危险操作（如删除系统文件、格式化磁盘等）
        - 对于有风险的操作，先警告用户并解释后果
        - 如果用户请求不清楚，先询问澄清

        ## 当前环境信息：
        {0}
        """;

    public OpenAIService()
    {
        _httpClient = new HttpClient();
        _settings = AppSettings.Load();
        UpdateHttpClient();
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.ApiKey);

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        UpdateHttpClient();
    }

    private void UpdateHttpClient()
    {
        _httpClient.DefaultRequestHeaders.Clear();
        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        }
    }

    /// <summary>
    /// 发送对话消息（支持历史上下文）
    /// </summary>
    public async Task<ChatResponse> SendChatAsync(
        string userMessage, 
        IEnumerable<ChatMessage> history,
        TerminalContext? terminalContext = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new ChatResponse 
            { 
                Content = "请先在设置中配置 API Key",
                IsError = true
            };
        }

        // 构建环境信息
        var contextInfo = terminalContext?.ToString() ?? "终端信息不可用";
        var systemMessage = string.Format(SystemPrompt, contextInfo);

        // 构建消息列表
        var messages = new List<object>
        {
            new { role = "system", content = systemMessage }
        };

        // 添加历史消息（限制数量以控制 token 消耗）
        var recentHistory = history.TakeLast(20).ToList();
        foreach (var msg in recentHistory)
        {
            if (msg.Role == "user" || msg.Role == "assistant")
            {
                var content = msg.Content;
                // 如果是助手消息且有生成的命令，将命令附加到内容中
                if (msg.Role == "assistant" && msg.HasCommand)
                {
                    content = $"{msg.Content}\n\n```powershell\n{msg.GeneratedCommand}\n```";
                }
                messages.Add(new { role = msg.Role, content });
            }
        }

        // 添加当前用户消息
        messages.Add(new { role = "user", content = userMessage });

        var requestBody = new
        {
            model = _settings.Model,
            messages,
            temperature = _settings.Temperature,
            max_tokens = _settings.MaxTokens
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content1 = new StringContent(json, Encoding.UTF8, "application/json");

        var baseUrl = _settings.ApiBaseUrl.TrimEnd('/');
        var endpoint = $"{baseUrl}/chat/completions";

        try
        {
            var response = await _httpClient.PostAsync(endpoint, content1, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new ChatResponse
                {
                    Content = $"API 错误 ({response.StatusCode}): {responseJson}",
                    IsError = true
                };
            }

            var result = JsonSerializer.Deserialize<OpenAIResponse>(responseJson);
            var responseContent = result?.Choices?.FirstOrDefault()?.Message?.Content ?? "无法获取响应";

            // 解析响应，提取命令（如果有）
            var (textContent, command) = ParseResponse(responseContent);

            return new ChatResponse
            {
                Content = textContent,
                Command = command,
                HasCommand = !string.IsNullOrWhiteSpace(command)
            };
        }
        catch (TaskCanceledException)
        {
            return new ChatResponse
            {
                Content = "请求已取消",
                IsError = true
            };
        }
        catch (Exception ex)
        {
            return new ChatResponse
            {
                Content = $"错误: {ex.Message}",
                IsError = true
            };
        }
    }

    /// <summary>
    /// 解析响应，提取文本和命令
    /// </summary>
    private static (string text, string? command) ParseResponse(string response)
    {
        // 查找 powershell 代码块
        var pattern = @"```(?:powershell|ps1|ps)?\s*\n([\s\S]*?)```";
        var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var matches = regex.Matches(response);

        if (matches.Count == 0)
        {
            return (response.Trim(), null);
        }

        // 提取所有命令（可能有多个代码块）
        var commands = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var cmd = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(cmd))
            {
                commands.Add(cmd);
            }
        }

        // 移除代码块，保留文本说明
        var textContent = regex.Replace(response, "").Trim();
        
        // 清理多余的空行
        textContent = System.Text.RegularExpressions.Regex.Replace(textContent, @"\n{3,}", "\n\n").Trim();

        // 合并所有命令
        var combinedCommand = commands.Count > 0 ? string.Join("\n", commands) : null;

        return (textContent, combinedCommand);
    }

    // 保留原有的简单接口以保持兼容性
    public async Task<string> GenerateCommandAsync(string userPrompt, string? currentDirectory = null, CancellationToken cancellationToken = default)
    {
        var context = new TerminalContext { CurrentDirectory = currentDirectory ?? "未知" };
        var response = await SendChatAsync(userPrompt, Enumerable.Empty<ChatMessage>(), context, cancellationToken);
        
        if (response.HasCommand)
        {
            return response.Command!;
        }
        
        return response.Content;
    }
}

/// <summary>
/// AI 聊天响应
/// </summary>
public class ChatResponse
{
    /// <summary>
    /// 文本回复内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 提取的 PowerShell 命令（如果有）
    /// </summary>
    public string? Command { get; set; }

    /// <summary>
    /// 是否包含可执行命令
    /// </summary>
    public bool HasCommand { get; set; }

    /// <summary>
    /// 是否为错误响应
    /// </summary>
    public bool IsError { get; set; }
}

// OpenAI API 响应模型
public class OpenAIResponse
{
    [JsonPropertyName("choices")]
    public List<Choice>? Choices { get; set; }
}

public class Choice
{
    [JsonPropertyName("message")]
    public Message? Message { get; set; }
}

public class Message
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}
