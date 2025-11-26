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
        你是一个专业的 PowerShell 命令生成助手。用户会用自然语言描述他们想要完成的任务，你需要生成对应的 PowerShell 命令。

        规则：
        1. 只输出 PowerShell 命令，不要有任何解释或其他文字
        2. 如果需要多条命令，用分号或换行分隔
        3. 确保命令安全，不要执行危险操作（如删除系统文件、格式化磁盘等）
        4. 命令应该简洁高效
        5. 如果用户的请求不清楚或不安全，生成一个安全的替代命令并在命令前加上注释说明

        当前工作目录：{0}
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

    public async Task<string> GenerateCommandAsync(string userPrompt, string? currentDirectory = null, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return "# 错误：请先在设置中配置 API Key";
        }

        var systemMessage = string.Format(SystemPrompt, currentDirectory ?? "未知");

        var requestBody = new
        {
            model = _settings.Model,
            messages = new[]
            {
                new { role = "system", content = systemMessage },
                new { role = "user", content = userPrompt }
            },
            temperature = _settings.Temperature,
            max_tokens = _settings.MaxTokens
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var baseUrl = _settings.ApiBaseUrl.TrimEnd('/');
        var endpoint = $"{baseUrl}/chat/completions";

        try
        {
            var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return $"# API 错误 ({response.StatusCode}): {responseJson}";
            }

            var result = JsonSerializer.Deserialize<OpenAIResponse>(responseJson);
            var command = result?.Choices?.FirstOrDefault()?.Message?.Content ?? "# 无法生成命令";
            
            return command.Trim();
        }
        catch (TaskCanceledException)
        {
            return "# 请求已取消";
        }
        catch (Exception ex)
        {
            return $"# 错误: {ex.Message}";
        }
    }
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

