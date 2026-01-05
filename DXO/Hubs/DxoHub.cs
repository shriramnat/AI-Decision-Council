using Microsoft.AspNetCore.SignalR;
using DXO.Models;
using DXO.Services.NativeAgent;

namespace DXO.Hubs;

/// <summary>
/// SignalR hub client interface for DXO real-time updates
/// </summary>
public interface IDxoHubClient
{
    // Session lifecycle events
    Task SessionStarted(Guid sessionId);
    Task SessionPaused(Guid sessionId);
    Task SessionStopped(Guid sessionId, string reason);
    Task SessionCompleted(Guid sessionId, string finalContent, string stopReason);
    Task SessionError(Guid sessionId, string error);

    // Iteration events
    Task IterationStarted(Guid sessionId, int iteration);
    Task IterationCompleted(Guid sessionId, int iteration);

    // Message streaming events
    Task MessageStarted(Guid sessionId, Guid messageId, string persona, int iteration);
    Task MessageChunk(Guid sessionId, Guid messageId, string content);
    Task MessageCompleted(Guid sessionId, Guid messageId, string fullContent);

    // Memory events
    Task PersonaMemoryReset(Guid sessionId, string persona);
}

/// <summary>
/// SignalR hub for DXO real-time communication
/// </summary>
public class DxoHub : Hub<IDxoHubClient>
{
    private readonly ILogger<DxoHub> _logger;
    private readonly IReviewerRecommendationService? _recommendationService;

    public DxoHub(ILogger<DxoHub> logger, IReviewerRecommendationService? recommendationService = null)
    {
        _logger = logger;
        _recommendationService = recommendationService;
    }

    /// <summary>
    /// Join a session group to receive updates for that session
    /// </summary>
    public async Task JoinSession(Guid sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId.ToString());
        _logger.LogDebug("Client {ConnectionId} joined session {SessionId}", Context.ConnectionId, sessionId);
    }

    /// <summary>
    /// Leave a session group
    /// </summary>
    public async Task LeaveSession(Guid sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId.ToString());
        _logger.LogDebug("Client {ConnectionId} left session {SessionId}", Context.ConnectionId, sessionId);
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogDebug("Client {ConnectionId} connected", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception != null)
        {
            _logger.LogWarning(exception, "Client {ConnectionId} disconnected with error", Context.ConnectionId);
        }
        else
        {
            _logger.LogDebug("Client {ConnectionId} disconnected", Context.ConnectionId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Gets recommended reviewers for a given topic
    /// </summary>
    public async Task<GetReviewerRecommendationsResponse> GetReviewerRecommendations(string topic)
    {
        if (_recommendationService == null)
        {
            return new GetReviewerRecommendationsResponse
            {
                Success = false,
                ErrorCode = "SRV-001",
                ErrorMessage = "Recommendation service not configured",
                Reviewers = new List<ReviewerTemplateDto>()
            };
        }

        try
        {
            var userId = Context.User?.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")?.Value
                      ?? Context.User?.FindFirst("email")?.Value
                      ?? "unknown";

            var reviewers = await _recommendationService.GetRecommendedReviewersAsync(topic, userId);

            return new GetReviewerRecommendationsResponse
            {
                Success = true,
                Reviewers = reviewers
            };
        }
        catch (RecommendationException ex)
        {
            _logger.LogError(ex, "Recommendation failed with error code {ErrorCode}", ex.ErrorCode);
            return new GetReviewerRecommendationsResponse
            {
                Success = false,
                ErrorCode = ex.ErrorCode,
                ErrorMessage = ex.Message,
                Reviewers = new List<ReviewerTemplateDto>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during recommendation");
            var errorCode = "ERR-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
            return new GetReviewerRecommendationsResponse
            {
                Success = false,
                ErrorCode = errorCode,
                ErrorMessage = "An unexpected error occurred",
                Reviewers = new List<ReviewerTemplateDto>(),
                StackTrace = ex.StackTrace
            };
        }
    }

    /// <summary>
    /// Gets the status of Native Agent configuration
    /// </summary>
    public async Task<GetNativeAgentStatusResponse> GetNativeAgentStatus()
    {
        if (_recommendationService == null)
        {
            return new GetNativeAgentStatusResponse
            {
                Configured = false,
                UsingDefault = false,
                ModelName = null
            };
        }

        try
        {
            var userId = Context.User?.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")?.Value
                      ?? Context.User?.FindFirst("email")?.Value
                      ?? "unknown";

            var (configured, usingDefault, modelName) = await _recommendationService.GetNativeAgentStatusAsync(userId);

            return new GetNativeAgentStatusResponse
            {
                Configured = configured,
                UsingDefault = usingDefault,
                ModelName = modelName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Native Agent status");
            return new GetNativeAgentStatusResponse
            {
                Configured = false,
                UsingDefault = false,
                ModelName = null
            };
        }
    }
}
