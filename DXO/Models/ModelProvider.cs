namespace DXO.Models;

/// <summary>
/// Enum for supported AI model providers
/// </summary>
public enum ModelProvider
{
    OpenAI,
    Azure,
    Google,
    XAI,
    Anthropic
}

/// <summary>
/// Extension methods for ModelProvider
/// </summary>
public static class ModelProviderExtensions
{
    public static string ToDisplayString(this ModelProvider provider)
    {
        return provider switch
        {
            ModelProvider.OpenAI => "OpenAI",
            ModelProvider.Azure => "Azure",
            ModelProvider.Google => "Google",
            ModelProvider.XAI => "xAI",
            ModelProvider.Anthropic => "Anthropic",
            _ => provider.ToString()
        };
    }

    public static ModelProvider FromString(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return ModelProvider.OpenAI;
            
        return provider.Trim().ToLowerInvariant() switch
        {
            "openai" => ModelProvider.OpenAI,
            "azure" => ModelProvider.Azure,
            "google" => ModelProvider.Google,
            "xai" => ModelProvider.XAI,
            "anthropic" => ModelProvider.Anthropic,
            _ => ModelProvider.OpenAI // default
        };
    }
}
