namespace DXO.Services.Settings;

/// <summary>
/// Service for managing application settings at runtime
/// </summary>
public interface ISettingsService
{
    // OpenAI settings
    string? GetApiKey();
    void SetApiKey(string? apiKey);
    bool HasApiKey();
    
    // DeepSeek/Azure AI Foundry settings
    string? GetDeepSeekApiKey();
    void SetDeepSeekApiKey(string? apiKey);
    string? GetDeepSeekEndpoint();
    void SetDeepSeekEndpoint(string? endpoint);
    bool HasDeepSeekConfiguration();
}

public class SettingsService : ISettingsService
{
    private string? _apiKey;
    private string? _deepSeekApiKey;
    private string? _deepSeekEndpoint;

    // OpenAI API Key
    public string? GetApiKey()
    {
        return _apiKey;
    }

    public void SetApiKey(string? apiKey)
    {
        _apiKey = apiKey?.Trim();
    }

    public bool HasApiKey()
    {
        return !string.IsNullOrWhiteSpace(_apiKey);
    }

    // DeepSeek API Key
    public string? GetDeepSeekApiKey()
    {
        return _deepSeekApiKey;
    }

    public void SetDeepSeekApiKey(string? apiKey)
    {
        _deepSeekApiKey = apiKey?.Trim();
    }

    // DeepSeek Endpoint
    public string? GetDeepSeekEndpoint()
    {
        return _deepSeekEndpoint;
    }

    public void SetDeepSeekEndpoint(string? endpoint)
    {
        _deepSeekEndpoint = endpoint?.Trim();
    }

    public bool HasDeepSeekConfiguration()
    {
        return !string.IsNullOrWhiteSpace(_deepSeekApiKey) && !string.IsNullOrWhiteSpace(_deepSeekEndpoint);
    }
}
