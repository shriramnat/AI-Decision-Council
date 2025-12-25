namespace DXO.Configuration;

/// <summary>
/// Root configuration options for DXO
/// </summary>
public class DxoOptions
{
    public const string SectionName = "DXO";

    public string DefaultModelCreator { get; set; } = "gpt-4o";
    public string DefaultModelReviewer { get; set; } = "gpt-4o";
    public int RequestTimeoutSeconds { get; set; } = 60;
    public int MaxRetries { get; set; } = 2;
    public ModelConfigOptions[] Models { get; set; } = Array.Empty<ModelConfigOptions>();
    public OrchestrationOptions Orchestration { get; set; } = new();
    public PersistenceOptions Persistence { get; set; } = new();
    public RateLimitingOptions RateLimiting { get; set; } = new();
}

/// <summary>
/// Configuration for an individual AI model
/// </summary>
public class ModelConfigOptions
{
    public string ModelName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string Provider { get; set; } = "OpenAI";
    
    /// <summary>
    /// [DEPRECATED] Use Provider instead. Kept for backward compatibility.
    /// </summary>
    [Obsolete("Use Provider property instead")]
    public bool IsAzureModel 
    { 
        get => Provider?.ToLowerInvariant() == "azure";
        set => Provider = value ? "Azure" : "OpenAI";
    }
    
    public string? ApiKey { get; set; }
}

/// <summary>
/// Orchestration configuration options
/// </summary>
public class OrchestrationOptions
{
    public int DefaultMaxIterations { get; set; } = 8;
    public string DefaultStopMarker { get; set; } = "FINAL:";
    public bool StopOnReviewerApproved { get; set; } = true;
    public int MaxPromptChars { get; set; } = 20000;
    public int MaxDraftChars { get; set; } = 50000;
    public int ContextTurnsToSend { get; set; } = 8;
}

/// <summary>
/// Persistence configuration options
/// </summary>
public class PersistenceOptions
{
    public bool Enabled { get; set; } = true;
    public string ConnectionString { get; set; } = "Data Source=dxo.db";
}

/// <summary>
/// Rate limiting configuration options
/// </summary>
public class RateLimitingOptions
{
    public int PermitLimit { get; set; } = 10;
    public int WindowSeconds { get; set; } = 60;
}
