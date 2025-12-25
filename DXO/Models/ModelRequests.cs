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
