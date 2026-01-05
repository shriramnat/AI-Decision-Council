using System.Text.Json.Serialization;
using DXO.Services.NativeAgent;

namespace DXO.Hubs;

/// <summary>
/// Response for reviewer recommendations
/// </summary>
public class GetReviewerRecommendationsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("stackTrace")]
    public string? StackTrace { get; set; }

    [JsonPropertyName("reviewers")]
    public List<ReviewerTemplateDto> Reviewers { get; set; } = new();
}

/// <summary>
/// Response for Native Agent status
/// </summary>
public class GetNativeAgentStatusResponse
{
    [JsonPropertyName("configured")]
    public bool Configured { get; set; }

    [JsonPropertyName("usingDefault")]
    public bool UsingDefault { get; set; }

    [JsonPropertyName("modelName")]
    public string? ModelName { get; set; }
}
