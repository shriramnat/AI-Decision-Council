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
    public NativeAgentOptions NativeAgent { get; set; } = new();
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

/// <summary>
/// Native Agent configuration options for AI-powered features
/// </summary>
public class NativeAgentOptions
{
    public bool Enabled { get; set; } = false;
    public string ModelProvider { get; set; } = "OpenAI";
    public string Endpoint { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public string ModelName { get; set; } = string.Empty;
    
    /// <summary>
    /// Scriban template for reviewer recommendation prompt with {{topic}} and {{reviewers}} placeholders
    /// </summary>
    public string RecommendationPrompt { get; set; } = @"You are an intelligent agent tasked with analyzing discussion topics and recommending appropriate expert reviewers.

Analyze the following topic and select up to 5 most relevant reviewers from the available options.

## Topic:
{{topic}}

## Available Reviewers:
{{for reviewer in reviewers}}
- **{{reviewer.agentId}}** ({{reviewer.role}}, Category: {{reviewer.category}})
  {{reviewer.promptPreview}}

{{end}}

## Task:
Evaluate which reviewers would be most relevant and beneficial for discussing this topic. Consider:
1. Relevance to the topic domain
2. Diversity of perspectives
3. Complementary expertise
4. Coverage of different aspects (technical, structural, domain, decision-readiness, risk, etc.)

Return ONLY a valid JSON response in this exact format (no additional text):
{
  ""recommendedReviewers"": [""agentId1"", ""agentId2"", ""agentId3"", ""agentId4"", ""agentId5""]
}

Provide up to 5 reviewer IDs. Use only IDs from the available list above.";
}

