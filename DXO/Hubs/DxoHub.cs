using Microsoft.AspNetCore.SignalR;
using DXO.Models;

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

    public DxoHub(ILogger<DxoHub> logger)
    {
        _logger = logger;
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
}
