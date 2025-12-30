namespace DXO.Models;

/// <summary>
/// Request to add a new model
/// </summary>
public record AddModelRequest
{
    public string ModelName { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public string? ApiKey { get; init; }
    public string? Provider { get; init; }
    public bool IsAzureModel { get; init; }
    public string? DisplayName { get; init; }
}

/// <summary>
/// Request to update an existing model
/// </summary>
public record UpdateModelRequest
{
    public string ModelName { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public string? ApiKey { get; init; }
    public string? Provider { get; init; }
    public bool IsAzureModel { get; init; }
    public string? DisplayName { get; init; }
}

/// <summary>
/// Request to submit user feedback for a specific iteration
/// </summary>
public record SubmitFeedbackRequest
{
    public int Iteration { get; init; }
    public string Feedback { get; init; } = string.Empty;
}

/// <summary>
/// Request to iterate with feedback after session completion
/// </summary>
public record IterateWithFeedbackRequest
{
    public string Comments { get; init; } = string.Empty;
    public string? Tone { get; init; }
    public string? Length { get; init; }
    public string? Audience { get; init; }
    public int MaxAdditionalIterations { get; init; } = 1;
}
