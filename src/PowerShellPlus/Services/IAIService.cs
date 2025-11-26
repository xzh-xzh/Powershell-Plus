namespace PowerShellPlus.Services;

public interface IAIService
{
    Task<string> GenerateCommandAsync(string userPrompt, string? currentDirectory = null, CancellationToken cancellationToken = default);
    bool IsConfigured { get; }
}

