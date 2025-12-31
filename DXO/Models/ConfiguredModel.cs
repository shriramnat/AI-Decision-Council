namespace DXO.Models;

/// <summary>
/// Represents a configured AI model with its endpoint and API key settings
/// </summary>
public class ConfiguredModel
{
    public int Id { get; set; }
    
    /// <summary>
    /// Unique name/identifier for the model (e.g., "gpt-4", "DeepSeek-V3")
    /// </summary>
    public string ModelName { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name for the model (optional, defaults to ModelName)
    /// </summary>
    public string? DisplayName { get; set; }
    
    /// <summary>
    /// The API endpoint URL for this model
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;
    
    /// <summary>
    /// The provider for this model (OpenAI, Azure, Google, xAI, Anthropic)
    /// </summary>
    public ModelProvider Provider { get; set; } = ModelProvider.OpenAI;
    
    /// <summary>
    /// The encrypted API key for this model (encrypted using Data Protection API)
    /// </summary>
    public string? ApiKey { get; set; }
    
    /// <summary>
    /// The email address of the user who owns this model configuration
    /// </summary>
    public string UserEmail { get; set; } = string.Empty;
    
    /// <summary>
    /// [DEPRECATED] Use Provider instead. Kept for backward compatibility during migration.
    /// Whether this is an Azure-hosted model (uses different API format)
    /// </summary>
    [Obsolete("Use Provider property instead")]
    public bool IsAzureModel 
    { 
        get => Provider == ModelProvider.Azure;
        set => Provider = value ? ModelProvider.Azure : ModelProvider.OpenAI;
    }
    
    /// <summary>
    /// When this model was added to the system
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When this model was last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}