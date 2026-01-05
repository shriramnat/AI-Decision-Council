using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scriban;
using DXO.Configuration;
using DXO.Data;
using DXO.Models;
using DXO.Services.Models;
using DXO.Services.OpenAI;

namespace DXO.Services.NativeAgent;

/// <summary>
/// DTO for reviewer template used in recommendations
/// </summary>
public class ReviewerTemplateDto
{
    [JsonPropertyName("agentId")]
    public string AgentId { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("promptPreview")]
    public string PromptPreview { get; set; } = string.Empty;
}

/// <summary>
/// Service for getting AI-powered reviewer recommendations
/// </summary>
public interface IReviewerRecommendationService
{
    Task<List<ReviewerTemplateDto>> GetRecommendedReviewersAsync(string topic, string userId, CancellationToken cancellationToken = default);
    Task<(bool Configured, bool UsingDefault, string? ModelName)> GetNativeAgentStatusAsync(string userId);
}

public class ReviewerRecommendationService : IReviewerRecommendationService
{
    private readonly IModelManagementService _modelManagementService;
    private readonly IOpenAIService _openAIService;
    private readonly ILogger<ReviewerRecommendationService> _logger;
    private readonly DxoOptions _options;
    private readonly DxoDbContext _dbContext;
    
    private static bool _connectedTested = false;
    private static bool _connectionValid = false;

    public ReviewerRecommendationService(
        IModelManagementService modelManagementService,
        IOpenAIService openAIService,
        IOptions<DxoOptions> options,
        ILogger<ReviewerRecommendationService> logger,
        DxoDbContext dbContext)
    {
        _modelManagementService = modelManagementService;
        _openAIService = openAIService;
        _logger = logger;
        _options = options.Value;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Gets recommended reviewers for a given topic using the Native Agent
    /// </summary>
    public async Task<List<ReviewerTemplateDto>> GetRecommendedReviewersAsync(
        string topic,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new ArgumentException("Topic cannot be empty", nameof(topic));
        }

        try
        {
            // Lazy validation: test connectivity on first call
            if (!_connectedTested)
            {
                _connectionValid = await ValidateNativeAgentConnectionAsync(userId, cancellationToken);
                _connectedTested = true;
            }

            if (!_connectionValid)
            {
                var errorCode = GenerateErrorCode();
                var message = "Native Agent is not properly configured or connection failed";
                _logger.LogError("NativeAgent connection invalid. ErrorCode: {ErrorCode}", errorCode);
                throw new RecommendationException(errorCode, message);
            }

            // Load reviewer templates from agentconfigurations.json
            var reviewerTemplates = await LoadReviewerTemplatesAsync();

            // Get Native Agent model configuration
            var nativeAgentModel = await GetNativeAgentModelAsync(userId);
            if (nativeAgentModel == null)
            {
                var errorCode = GenerateErrorCode();
                var message = "Native Agent model configuration not found";
                _logger.LogError("Native Agent model not found for user {UserId}. ErrorCode: {ErrorCode}", userId, errorCode);
                throw new RecommendationException(errorCode, message);
            }

            // Render prompt using Scriban
            var renderedPrompt = RenderRecommendationPrompt(topic, reviewerTemplates);
            
            _logger.LogInformation("=== NATIVE AGENT REQUEST ===");
            _logger.LogInformation("Topic: {Topic}", topic);
            _logger.LogInformation("Prompt Length: {Length} characters", renderedPrompt.Length);
            _logger.LogInformation("Rendered Prompt:\n{Prompt}", renderedPrompt);
            _logger.LogInformation("===========================");

            // Call Native Agent to get recommendations
            var recommendedAgentIds = await CallNativeAgentAsync(
                nativeAgentModel,
                renderedPrompt,
                userId,
                cancellationToken);

            // Validate and return matched reviewer templates
            var recommendations = reviewerTemplates
                .Where(r => recommendedAgentIds.Contains(r.AgentId, StringComparer.OrdinalIgnoreCase))
                .ToList();

            _logger.LogInformation(
                "Successfully recommended {Count} reviewers for user {UserId}. AgentIds: {AgentIds}",
                recommendations.Count,
                userId,
                string.Join(", ", recommendations.Select(r => r.AgentId)));

            return recommendations;
        }
        catch (RecommendationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var errorCode = GenerateErrorCode();
            _logger.LogError(
                ex,
                "Unexpected error during reviewer recommendation for user {UserId}. ErrorCode: {ErrorCode}",
                userId,
                errorCode);
            throw new RecommendationException(errorCode, "An unexpected error occurred during recommendation", ex);
        }
    }

    /// <summary>
    /// Gets the current Native Agent configuration status
    /// </summary>
    public async Task<(bool Configured, bool UsingDefault, string? ModelName)> GetNativeAgentStatusAsync(string userId)
    {
        try
        {
            // Check if default config exists from environment
            var hasDefault = !string.IsNullOrWhiteSpace(_options.NativeAgent.Endpoint) &&
                            !string.IsNullOrWhiteSpace(_options.NativeAgent.ModelName) &&
                            _options.NativeAgent.Enabled;

            // Check if user has custom override
            var userSettings = await _dbContext.UserSettings
                .FirstOrDefaultAsync(us => us.UserId == userId);

            if (userSettings?.NativeAgentModelId.HasValue == true)
            {
                var model = await _dbContext.ConfiguredModels
                    .FirstOrDefaultAsync(m => m.Id == userSettings.NativeAgentModelId);

                if (model != null)
                {
                    return (true, false, model.DisplayName ?? model.ModelName);
                }
            }

            // No override - check if defaults are available
            if (hasDefault)
            {
                return (true, true, _options.NativeAgent.ModelName);
            }

            return (false, false, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving Native Agent status for user {UserId}", userId);
            return (false, false, null);
        }
    }

    /// <summary>
    /// Gets the Native Agent model configuration, preferring user override, falling back to defaults
    /// </summary>
    private async Task<ConfiguredModel?> GetNativeAgentModelAsync(string userId)
    {
        // Check for user override first
        var userSettings = await _dbContext.UserSettings
            .FirstOrDefaultAsync(us => us.UserId == userId);

        if (userSettings?.NativeAgentModelId.HasValue == true)
        {
            var overrideModel = await _dbContext.ConfiguredModels
                .FirstOrDefaultAsync(m => m.Id == userSettings.NativeAgentModelId && m.UserEmail == userId);

            if (overrideModel != null)
            {
                _logger.LogInformation("Using user override Native Agent model for user {UserId}", userId);
                return overrideModel;
            }
        }

        // Fall back to default from environment
        if (!string.IsNullOrWhiteSpace(_options.NativeAgent.Endpoint) &&
            !string.IsNullOrWhiteSpace(_options.NativeAgent.ModelName) &&
            _options.NativeAgent.Enabled)
        {
            _logger.LogInformation("Using default Native Agent configuration for user {UserId}", userId);
            
            // Create a temporary ConfiguredModel for the default
            return new ConfiguredModel
            {
                ModelName = _options.NativeAgent.ModelName,
                Endpoint = _options.NativeAgent.Endpoint,
                Provider = ModelProviderExtensions.FromString(_options.NativeAgent.ModelProvider),
                ApiKey = _options.NativeAgent.ApiKey,
                UserEmail = userId
            };
        }

        return null;
    }

    /// <summary>
    /// Validates Native Agent connectivity (lazy - only called once on first use)
    /// </summary>
    private async Task<bool> ValidateNativeAgentConnectionAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            var model = await GetNativeAgentModelAsync(userId);
            if (model == null)
            {
                _logger.LogWarning("Native Agent model configuration not found during validation");
                return false;
            }

            // Could send a test prompt here, but for now just verify config exists
            _logger.LogInformation("Native Agent configuration validated");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Native Agent validation failed");
            return false;
        }
    }

    /// <summary>
    /// Loads reviewer templates from agentconfigurations.json
    /// </summary>
    private async Task<List<ReviewerTemplateDto>> LoadReviewerTemplatesAsync()
    {
        try
        {
            var configPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "agentconfigurations.json");

            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException($"Agent configurations file not found at {configPath}");
            }

            var json = await File.ReadAllTextAsync(configPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var reviewers = new List<ReviewerTemplateDto>();

            if (root.TryGetProperty("agents", out var agentsElement) &&
                agentsElement.TryGetProperty("reviewers", out var reviewersArray))
            {
                foreach (var reviewer in reviewersArray.EnumerateArray())
                {
                    var agentId = reviewer.GetProperty("agentId").GetString() ?? string.Empty;
                    var role = reviewer.GetProperty("role").GetString() ?? string.Empty;
                    var category = reviewer.GetProperty("category").GetString() ?? string.Empty;
                    var prompt = reviewer.GetProperty("prompt").GetString() ?? string.Empty;

                    // Send first 500 chars as preview (enough for rubric but not overwhelming)
                    var preview = prompt.Length > 500
                        ? prompt.Substring(0, 500) + "..."
                        : prompt;

                    reviewers.Add(new ReviewerTemplateDto
                    {
                        AgentId = agentId,
                        Role = role,
                        Category = category,
                        PromptPreview = preview
                    });
                }
            }

            _logger.LogInformation("Loaded {Count} reviewer templates", reviewers.Count);
            return reviewers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading reviewer templates");
            throw;
        }
    }

    /// <summary>
    /// Renders the recommendation prompt using Scriban templating
    /// </summary>
    private string RenderRecommendationPrompt(string topic, List<ReviewerTemplateDto> reviewers)
    {
        try
        {
            var template = Template.Parse(_options.NativeAgent.RecommendationPrompt);
            var result = template.Render(new { topic, reviewers });
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rendering recommendation prompt");
            throw;
        }
    }

    /// <summary>
    /// Calls the Native Agent and parses the JSON response
    /// </summary>
    private async Task<List<string>> CallNativeAgentAsync(
        ConfiguredModel model,
        string prompt,
        string userId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Decrypt API key if needed
            string? apiKey = model.ApiKey;
            if (!string.IsNullOrWhiteSpace(apiKey) && model.ApiKey != _options.NativeAgent.ApiKey)
            {
                // User override - need to decrypt
                // For now, assume it's already decrypted or handle appropriately
                // In real implementation, would need encryption service
            }

            var request = new ChatCompletionRequest
            {
                Model = model.ModelName,
                Messages = new List<ChatMessageDto>
                {
                    ChatMessageDto.User(prompt)
                },
                Temperature = 0.5,
                MaxTokens = 1000,
                UserEmail = userId
            };

            var contentBuilder = new System.Text.StringBuilder();

            // Stream the response and collect content
            await foreach (var chunk in _openAIService.StreamChatCompletionAsync(request, cancellationToken))
            {
                var content = chunk.Choices.FirstOrDefault()?.Delta?.Content;
                if (!string.IsNullOrEmpty(content))
                {
                    contentBuilder.Append(content);
                }
            }

            var fullContent = contentBuilder.ToString().Trim();

            _logger.LogInformation("=== NATIVE AGENT RESPONSE ===");
            _logger.LogInformation("Response Length: {Length} characters", fullContent.Length);
            _logger.LogInformation("Raw Response:\n{Response}", fullContent);
            _logger.LogInformation("============================");

            // Extract JSON from response (handle cases where AI adds extra text)
            var jsonContent = ExtractJsonFromResponse(fullContent);
            
            _logger.LogInformation("Extracted JSON:\n{Json}", jsonContent);

            // Parse JSON response with strict contract
            var jsonResponse = JsonSerializer.Deserialize<RecommendationResponse>(jsonContent, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });

            if (jsonResponse == null || jsonResponse.RecommendedReviewers == null)
            {
                throw new JsonException("Response did not contain expected 'recommendedReviewers' array");
            }

            var recommendedIds = jsonResponse.RecommendedReviewers
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();

            // Limit to max 5
            if (recommendedIds.Count > 5)
            {
                recommendedIds = recommendedIds.Take(5).ToList();
            }

            _logger.LogInformation("Parsed {Count} reviewer IDs from response: {Ids}", 
                recommendedIds.Count, 
                string.Join(", ", recommendedIds));

            return recommendedIds;
        }
        catch (JsonException ex)
        {
            var errorCode = GenerateErrorCode();
            _logger.LogError(
                ex,
                "Failed to parse Native Agent JSON response. ErrorCode: {ErrorCode}",
                errorCode);
            throw new RecommendationException(
                errorCode,
                "Invalid JSON response from Native Agent. Please check the response format.",
                ex);
        }
        catch (Exception ex)
        {
            var errorCode = GenerateErrorCode();
            _logger.LogError(
                ex,
                "Error calling Native Agent. ErrorCode: {ErrorCode}",
                errorCode);
            throw new RecommendationException(
                errorCode,
                "Error calling Native Agent. Please try again.",
                ex);
        }
    }

    /// <summary>
    /// Extracts JSON from response, handling cases where AI adds extra text
    /// </summary>
    private string ExtractJsonFromResponse(string response)
    {
        // Try to find JSON object boundaries
        var startIndex = response.IndexOf('{');
        var endIndex = response.LastIndexOf('}');

        if (startIndex >= 0 && endIndex > startIndex)
        {
            return response.Substring(startIndex, endIndex - startIndex + 1);
        }

        // If no JSON found, return original (will fail parsing with clear error)
        return response;
    }

    /// <summary>
    /// Generates a unique error code for debugging
    /// </summary>
    private static string GenerateErrorCode()
    {
        return "ERR-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
    }
}

/// <summary>
/// Contract for Native Agent recommendation response
/// </summary>
internal class RecommendationResponse
{
    [JsonPropertyName("recommendedReviewers")]
    public List<string> RecommendedReviewers { get; set; } = new();
}