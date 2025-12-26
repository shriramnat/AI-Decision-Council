using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DXO.Configuration;
using DXO.Data;
using DXO.Hubs;
using DXO.Models;
using DXO.Services.OpenAI;
using Microsoft.AspNetCore.SignalR;

namespace DXO.Services.Orchestration;

/// <summary>
/// Service for orchestrating the Creator-Reviewer loop
/// </summary>
public interface IOrchestrationService
{
    Task<Session> CreateSessionAsync(CreateSessionRequest request, CancellationToken cancellationToken = default);
    Task<Session?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<List<Session>> GetSessionsAsync(CancellationToken cancellationToken = default);
    Task StartSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task StepSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task StopSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task ResetPersonaMemoryAsync(Guid sessionId, Persona persona, CancellationToken cancellationToken = default);
    void CancelSession(Guid sessionId);
}

public class OrchestrationService : IOrchestrationService
{
    private readonly DxoDbContext _dbContext;
    private readonly IOpenAIService _openAIService;
    private readonly IHubContext<DxoHub, IDxoHubClient> _hubContext;
    private readonly ILogger<OrchestrationService> _logger;
    private readonly DxoOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, CancellationTokenSource> _sessionCancellations = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, bool> _sessionNeedsFinalIteration = new();
    
    private const string ReviewerSystemPrompt = @"You are DXO Reviewer. Critique the draft for correctness, clarity, completeness, and rubric adherence. Provide actionable revisions and a checklist. If acceptable, include @@SIGNED OFF@@.";

    private const string SafetyPrompt = @"Do not request or reveal secrets (API keys, credentials). Avoid personal data; do not invent factual claims without evidence. If requirements are unclear, ask clarifying questions or state assumptions explicitly.";

    public OrchestrationService(
        DxoDbContext dbContext,
        IOpenAIService openAIService,
        IHubContext<DxoHub, IDxoHubClient> hubContext,
        IOptions<DxoOptions> options,
        ILogger<OrchestrationService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _dbContext = dbContext;
        _openAIService = openAIService;
        _hubContext = hubContext;
        _logger = logger;
        _options = options.Value;
        _scopeFactory = scopeFactory;
    }

    public async Task<Session> CreateSessionAsync(CreateSessionRequest request, CancellationToken cancellationToken = default)
    {
        var creatorConfig = new PersonaConfig
        {
            RootPrompt = request.CreatorRootPrompt ?? GetDefaultCreatorPrompt(),
            Model = request.CreatorModel ?? _options.DefaultModelCreator,
            Temperature = request.CreatorTemperature ?? 0.7,
            MaxOutputTokens = request.CreatorMaxTokens ?? 4096,
            TopP = request.CreatorTopP ?? 1.0,
            PresencePenalty = request.CreatorPresencePenalty ?? 0.0,
            FrequencyPenalty = request.CreatorFrequencyPenalty ?? 0.0
        };

        // Build reviewers list from request
        var reviewers = new List<ReviewerConfig>();
        if (request.Reviewers != null && request.Reviewers.Any())
        {
            foreach (var reviewer in request.Reviewers)
            {
                reviewers.Add(new ReviewerConfig
                {
                    Id = reviewer.Id ?? Guid.NewGuid().ToString(),
                    Name = reviewer.Name ?? $"Reviewer {reviewers.Count + 1}",
                    RootPrompt = reviewer.RootPrompt ?? GetDefaultReviewerPrompt(reviewers.Count + 1),
                    Model = reviewer.Model ?? _options.DefaultModelReviewer,
                    Temperature = reviewer.Temperature ?? 0.5,
                    MaxOutputTokens = reviewer.MaxTokens ?? 4096,
                    TopP = reviewer.TopP ?? 1.0,
                    PresencePenalty = reviewer.PresencePenalty ?? 0.0,
                    FrequencyPenalty = reviewer.FrequencyPenalty ?? 0.0
                });
            }
        }
        else
        {
            // Default: create one reviewer if none provided
            reviewers.Add(new ReviewerConfig
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Reviewer 1",
                RootPrompt = GetDefaultReviewerPrompt(1),
                Model = _options.DefaultModelReviewer,
                Temperature = 0.5,
                MaxOutputTokens = 4096,
                TopP = 1.0,
                PresencePenalty = 0.0,
                FrequencyPenalty = 0.0
            });
        }

        var session = new Session
        {
            Name = request.Name ?? "New Session",
            MaxIterations = request.MaxIterations ?? _options.Orchestration.DefaultMaxIterations,
            StopMarker = request.StopMarker ?? _options.Orchestration.DefaultStopMarker,
            StopOnReviewerApproved = request.StopOnReviewerApproved ?? _options.Orchestration.StopOnReviewerApproved,
            RunMode = request.RunMode ?? RunMode.Auto,
            Topic = request.Topic,
            CreatorConfigJson = JsonSerializer.Serialize(creatorConfig),
            ReviewersConfigJson = JsonSerializer.Serialize(reviewers)
        };

        _dbContext.Sessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created session {SessionId} with name {Name} and {ReviewerCount} reviewers", 
            session.SessionId, session.Name, reviewers.Count);

        return session;
    }

    public async Task<Session?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Sessions
            .Include(s => s.Messages.OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);
    }

    public async Task<List<Session>> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Sessions
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task StartSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session == null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        if (session.Status == SessionStatus.Running)
        {
            throw new InvalidOperationException($"Session {sessionId} is already running");
        }

        var cts = new CancellationTokenSource();
        _sessionCancellations[sessionId] = cts;

        // Run orchestration in background
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DxoDbContext>();
            
            try
            {
                await RunOrchestrationLoopAsync(sessionId, cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Session {SessionId} was cancelled", sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running session {SessionId}", sessionId);
                await UpdateSessionStatusAsync(dbContext, sessionId, SessionStatus.Error, StopReason.Error);
                await _hubContext.Clients.Group(sessionId.ToString()).SessionError(sessionId, ex.Message);
            }
            finally
            {
                _sessionCancellations.TryRemove(sessionId, out _);
            }
        }, cancellationToken);
    }

    public async Task StepSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session == null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        await RunSingleIterationAsync(sessionId, cancellationToken);
    }

    public async Task StopSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        CancelSession(sessionId);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DxoDbContext>();
        
        var session = await dbContext.Sessions.FindAsync(new object[] { sessionId }, cancellationToken);
        if (session != null && session.Status == SessionStatus.Running)
        {
            session.Status = SessionStatus.Stopped;
            session.StopReason = StopReason.UserStopped;
            session.UpdatedAt = DateTime.UtcNow;

            // Set final content from last creator message
            var lastCreatorMessage = await dbContext.Messages
                .Where(m => m.SessionId == sessionId && m.Persona == Persona.Creator)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (lastCreatorMessage != null)
            {
                session.FinalContent = lastCreatorMessage.Content;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.Group(sessionId.ToString()).SessionStopped(sessionId, StopReason.UserStopped.ToString());
        }
    }

    public void CancelSession(Guid sessionId)
    {
        if (_sessionCancellations.TryRemove(sessionId, out var cts))
        {
            cts.Cancel();
        }
    }

    public async Task DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        CancelSession(sessionId);

        var session = await _dbContext.Sessions.FindAsync(new object[] { sessionId }, cancellationToken);
        if (session != null)
        {
            _dbContext.Sessions.Remove(session);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Deleted session {SessionId}", sessionId);
        }
    }

    public async Task ResetPersonaMemoryAsync(Guid sessionId, Persona persona, CancellationToken cancellationToken = default)
    {
        var messagesToDelete = await _dbContext.Messages
            .Where(m => m.SessionId == sessionId && m.Persona == persona)
            .ToListAsync(cancellationToken);

        _dbContext.Messages.RemoveRange(messagesToDelete);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Reset {Persona} memory for session {SessionId}", persona, sessionId);
        await _hubContext.Clients.Group(sessionId.ToString()).PersonaMemoryReset(sessionId, persona.ToString());
    }

    private async Task RunOrchestrationLoopAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DxoDbContext>();

        var session = await dbContext.Sessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);

        if (session == null) return;

        session.Status = SessionStatus.Running;
        session.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        await _hubContext.Clients.Group(sessionId.ToString()).SessionStarted(sessionId);

        // Check if we need a final iteration flag
        _sessionNeedsFinalIteration.TryGetValue(sessionId, out bool needsFinalIteration);

        while (!cancellationToken.IsCancellationRequested && 
               (session.CurrentIteration < session.MaxIterations || needsFinalIteration))
        {
            var shouldStop = await RunSingleIterationInternalAsync(dbContext, session, cancellationToken);
            if (shouldStop) break;

            // Update the flag after each iteration
            _sessionNeedsFinalIteration.TryGetValue(sessionId, out needsFinalIteration);

            // If we just completed the final iteration after all reviewers signed off
            if (needsFinalIteration && session.CurrentIteration > session.MaxIterations)
            {
                session.Status = SessionStatus.Completed;
                session.StopReason = StopReason.ReviewerApproved;

                var lastCreatorMessage = session.Messages
                    .Where(m => m.Persona == Persona.Creator)
                    .OrderByDescending(m => m.CreatedAt)
                    .FirstOrDefault();

                session.FinalContent = lastCreatorMessage?.Content;
                session.UpdatedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);

                await _hubContext.Clients.Group(sessionId.ToString())
                    .SessionCompleted(sessionId, session.FinalContent ?? "", StopReason.ReviewerApproved.ToString());
                
                // Clean up flag
                _sessionNeedsFinalIteration.TryRemove(sessionId, out _);
                break;
            }

            if (session.RunMode == RunMode.Step)
            {
                session.Status = SessionStatus.Paused;
                await dbContext.SaveChangesAsync(cancellationToken);
                await _hubContext.Clients.Group(sessionId.ToString()).SessionPaused(sessionId);
                return;
            }
        }

        // Check if max iterations reached without final iteration needed
        if (session.CurrentIteration >= session.MaxIterations && 
            session.Status == SessionStatus.Running && 
            !needsFinalIteration)
        {
            session.Status = SessionStatus.Completed;
            session.StopReason = StopReason.MaxIterationsReached;

            var lastCreatorMessage = session.Messages
                .Where(m => m.Persona == Persona.Creator)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefault();

            session.FinalContent = lastCreatorMessage?.Content;
            session.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            await _hubContext.Clients.Group(sessionId.ToString())
                .SessionCompleted(sessionId, session.FinalContent ?? "", StopReason.MaxIterationsReached.ToString());
            
            // Clean up flag if it exists
            _sessionNeedsFinalIteration.TryRemove(sessionId, out _);
        }
    }

    private async Task RunSingleIterationAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DxoDbContext>();

        var session = await dbContext.Sessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);

        if (session == null) return;

        await RunSingleIterationInternalAsync(dbContext, session, cancellationToken);
    }

    private async Task<bool> RunSingleIterationInternalAsync(DxoDbContext dbContext, Session session, CancellationToken cancellationToken)
    {
        session.CurrentIteration++;
        session.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        await _hubContext.Clients.Group(session.SessionId.ToString()).IterationStarted(session.SessionId, session.CurrentIteration);

        // Get configurations
        var creatorConfig = JsonSerializer.Deserialize<PersonaConfig>(session.CreatorConfigJson) ?? new PersonaConfig();
        var reviewers = JsonSerializer.Deserialize<List<ReviewerConfig>>(session.ReviewersConfigJson) ?? new List<ReviewerConfig>();

        // Step 1: Creator generates draft
        var creatorContent = await GenerateCreatorResponseAsync(dbContext, session, creatorConfig, reviewers, cancellationToken);
        
        // Check for stop marker
        if (creatorContent.Contains(session.StopMarker))
        {
            session.Status = SessionStatus.Completed;
            session.StopReason = StopReason.FinalMarkerDetected;
            session.FinalContent = ExtractFinalContent(creatorContent, session.StopMarker);
            session.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            await _hubContext.Clients.Group(session.SessionId.ToString()).SessionCompleted(session.SessionId, session.FinalContent, StopReason.FinalMarkerDetected.ToString());
            return true;
        }

        // Step 2: All reviewers critique in parallel-ish (sequentially for now, but all get the same draft)
        var reviewerResults = new List<(ReviewerConfig Reviewer, string Content, bool Approved)>();
        foreach (var reviewer in reviewers)
        {
            var reviewerContent = await GenerateReviewerResponseAsync(dbContext, session, reviewer, creatorContent, cancellationToken);
            var approved = IsApprovalDetected(reviewerContent);
            reviewerResults.Add((reviewer, reviewerContent, approved));
        }

        // Check if ALL reviewers approved
        bool allApproved = reviewerResults.Count > 0 && reviewerResults.All(r => r.Approved);

        if (session.StopOnReviewerApproved && allApproved)
        {
            // Don't stop yet - mark that we need one final Creator iteration to incorporate feedback
            _sessionNeedsFinalIteration[session.SessionId] = true;
            _logger.LogInformation("All reviewers signed off for session {SessionId}. Will run one final Creator iteration.", session.SessionId);
        }

        await _hubContext.Clients.Group(session.SessionId.ToString()).IterationCompleted(session.SessionId, session.CurrentIteration);
        return false;
    }

    private async Task<string> GenerateCreatorResponseAsync(DxoDbContext dbContext, Session session, PersonaConfig config, List<ReviewerConfig> reviewers, CancellationToken cancellationToken)
    {
        var messages = BuildCreatorMessages(session, config, reviewers);

        var request = new ChatCompletionRequest
        {
            Model = config.Model,
            Messages = messages,
            Temperature = config.Temperature,
            MaxTokens = config.MaxOutputTokens,
            TopP = config.TopP,
            PresencePenalty = config.PresencePenalty,
            FrequencyPenalty = config.FrequencyPenalty
        };

        // Stream the response
        var contentBuilder = new System.Text.StringBuilder();
        var messageId = Guid.NewGuid();

        await _hubContext.Clients.Group(session.SessionId.ToString()).MessageStarted(session.SessionId, messageId, Persona.Creator.ToString(), session.CurrentIteration);

        await foreach (var chunk in _openAIService.StreamChatCompletionAsync(request, cancellationToken))
        {
            var content = chunk.Choices.FirstOrDefault()?.Delta?.Content;
            if (!string.IsNullOrEmpty(content))
            {
                contentBuilder.Append(content);
                await _hubContext.Clients.Group(session.SessionId.ToString()).MessageChunk(session.SessionId, messageId, content);
            }
        }

        var fullContent = contentBuilder.ToString();

        // Save message to database
        var message = new Message
        {
            MessageId = messageId,
            SessionId = session.SessionId,
            Persona = Persona.Creator,
            Role = MessageRole.Assistant,
            Content = fullContent,
            Iteration = session.CurrentIteration,
            ModelUsed = config.Model
        };

        dbContext.Messages.Add(message);
        await dbContext.SaveChangesAsync(cancellationToken);

        await _hubContext.Clients.Group(session.SessionId.ToString()).MessageCompleted(session.SessionId, messageId, fullContent);

        return fullContent;
    }

    private async Task<string> GenerateReviewerResponseAsync(DxoDbContext dbContext, Session session, ReviewerConfig reviewer, string creatorDraft, CancellationToken cancellationToken)
    {
        var messages = BuildReviewerMessages(session, reviewer, creatorDraft);

        var request = new ChatCompletionRequest
        {
            Model = reviewer.Model,
            Messages = messages,
            Temperature = reviewer.Temperature,
            MaxTokens = reviewer.MaxOutputTokens,
            TopP = reviewer.TopP,
            PresencePenalty = reviewer.PresencePenalty,
            FrequencyPenalty = reviewer.FrequencyPenalty
        };

        // Stream the response
        var contentBuilder = new System.Text.StringBuilder();
        var messageId = Guid.NewGuid();

        // Use reviewer ID as persona identifier for SignalR
        await _hubContext.Clients.Group(session.SessionId.ToString()).MessageStarted(session.SessionId, messageId, reviewer.Id, session.CurrentIteration);

        await foreach (var chunk in _openAIService.StreamChatCompletionAsync(request, cancellationToken))
        {
            var content = chunk.Choices.FirstOrDefault()?.Delta?.Content;
            if (!string.IsNullOrEmpty(content))
            {
                contentBuilder.Append(content);
                await _hubContext.Clients.Group(session.SessionId.ToString()).MessageChunk(session.SessionId, messageId, content);
            }
        }

        var fullContent = contentBuilder.ToString();

        // Save message to database - use System persona with reviewer ID in metadata
        var message = new Message
        {
            MessageId = messageId,
            SessionId = session.SessionId,
            Persona = Persona.System, // Using System as a generic reviewer persona
            Role = MessageRole.Assistant,
            Content = fullContent,
            Iteration = session.CurrentIteration,
            ModelUsed = reviewer.Model,
            ReviewerId = reviewer.Id,
            ReviewerName = reviewer.Name
        };

        dbContext.Messages.Add(message);
        await dbContext.SaveChangesAsync(cancellationToken);

        await _hubContext.Clients.Group(session.SessionId.ToString()).MessageCompleted(session.SessionId, messageId, fullContent);

        return fullContent;
    }

    private List<ChatMessageDto> BuildCreatorMessages(Session session, PersonaConfig config, List<ReviewerConfig> reviewers)
    {
        var messages = new List<ChatMessageDto>
        {
            ChatMessageDto.System(config.RootPrompt),
            ChatMessageDto.System(SafetyPrompt)
        };

        // Add the central topic if provided
        if (!string.IsNullOrWhiteSpace(session.Topic))
        {
            messages.Add(ChatMessageDto.System($"=== CENTRAL DISCUSSION TOPIC ===\nThe following is the central topic that you should focus your work on. This topic defines what you should create content about:\n\n{session.Topic}\n\n=== END TOPIC ==="));
        }

        // Add context from previous turns
        var recentMessages = session.Messages
            .OrderByDescending(m => m.CreatedAt)
            .Take(_options.Orchestration.ContextTurnsToSend)
            .OrderBy(m => m.CreatedAt)
            .ToList();

        foreach (var msg in recentMessages)
        {
            if (msg.Persona == Persona.Creator)
            {
                messages.Add(ChatMessageDto.Assistant(msg.Content));
            }
            else if (!string.IsNullOrEmpty(msg.ReviewerId))
            {
                // Dynamic reviewer feedback
                var reviewerName = msg.ReviewerName ?? msg.ReviewerId;
                messages.Add(ChatMessageDto.User($"{reviewerName} feedback:\n{msg.Content}"));
            }
            else if (msg.Persona == Persona.Reviewer1)
            {
                messages.Add(ChatMessageDto.User($"Reviewer 1 feedback:\n{msg.Content}"));
            }
            else if (msg.Persona == Persona.Reviewer2)
            {
                messages.Add(ChatMessageDto.User($"Reviewer 2 feedback:\n{msg.Content}"));
            }
        }

        // If first iteration, add instruction to start drafting
        if (session.CurrentIteration == 1)
        {
            var instruction = !string.IsNullOrWhiteSpace(session.Topic)
                ? "Please begin drafting the content based on the CENTRAL DISCUSSION TOPIC provided above. Create a comprehensive first draft that addresses the topic thoroughly."
                : "Please begin drafting the content based on your root prompt. Create a comprehensive first draft.";
            messages.Add(ChatMessageDto.User(instruction));
        }
        else
        {
            var reviewerCount = reviewers.Count;
            var feedbackInstruction = reviewerCount == 1 
                ? "Please revise your draft incorporating feedback from the reviewer above."
                : $"Please revise your draft incorporating feedback from all {reviewerCount} reviewers above.";
            messages.Add(ChatMessageDto.User(feedbackInstruction));
        }

        return messages;
    }

    private List<ChatMessageDto> BuildReviewerMessages(Session session, ReviewerConfig reviewer, string latestDraft)
    {
        var messages = new List<ChatMessageDto>
        {
            ChatMessageDto.System(reviewer.RootPrompt),
            ChatMessageDto.System($"You are DXO {reviewer.Name}. " + ReviewerSystemPrompt),
            ChatMessageDto.System(SafetyPrompt)
        };

        // Add the central topic context if provided so reviewer understands what the content should address
        if (!string.IsNullOrWhiteSpace(session.Topic))
        {
            messages.Add(ChatMessageDto.System($"=== CENTRAL DISCUSSION TOPIC ===\nThe following is the central topic that the content should address. Use this to evaluate whether the draft adequately covers the intended topic:\n\n{session.Topic}\n\n=== END TOPIC ==="));
        }

        // Add recent context of this reviewer's previous reviews
        var recentMessages = session.Messages
            .Where(m => m.ReviewerId == reviewer.Id)
            .OrderByDescending(m => m.CreatedAt)
            .Take(_options.Orchestration.ContextTurnsToSend / 2)
            .OrderBy(m => m.CreatedAt)
            .ToList();

        foreach (var msg in recentMessages)
        {
            messages.Add(ChatMessageDto.Assistant(msg.Content));
        }

        messages.Add(ChatMessageDto.User($"Please review the following draft:\n\n{latestDraft}"));

        return messages;
    }

    /// <summary>
    /// Detects if the content contains a genuine @@SIGNED OFF@@ signal.
    /// Uses exact pattern matching and excludes negated forms.
    /// </summary>
    private static bool IsApprovalDetected(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        // Pattern explanation:
        // (?<![Nn][Oo][Tt]\s*) - Negative lookbehind: not preceded by "NOT " or "Not " (with optional whitespace)
        // (?<![Nn][Oo]\s+) - Negative lookbehind: not preceded by "NO " 
        // (?<![Nn][Ee][Vv][Ee][Rr]\s+) - Negative lookbehind: not preceded by "NEVER "
        // @@SIGNED OFF@@ - The exact approval marker
        var approvalPattern = @"(?<![Nn][Oo][Tt]\s*)(?<![Nn][Oo]\s+)(?<![Nn][Ee][Vv][Ee][Rr]\s+)@@SIGNED OFF@@";
        
        return Regex.IsMatch(content, approvalPattern, RegexOptions.IgnoreCase);
    }

    private static string ExtractFinalContent(string content, string stopMarker)
    {
        var index = content.IndexOf(stopMarker, StringComparison.Ordinal);
        if (index >= 0)
        {
            return content.Substring(index + stopMarker.Length).Trim();
        }
        return content;
    }

    private static async Task UpdateSessionStatusAsync(DxoDbContext dbContext, Guid sessionId, SessionStatus status, StopReason reason)
    {
        var session = await dbContext.Sessions.FindAsync(sessionId);
        if (session != null)
        {
            session.Status = status;
            session.StopReason = reason;
            session.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();
        }
    }

    private static string GetDefaultCreatorPrompt()
    {
        return @"You are DXO Creator, an expert technical author. Your job is to write high-quality technical content for a technical audience.

Authoring rules:
1) Structure the content with clear sections appropriate to the topic.
2) Use precise technical language and define terms on first use.
3) Do not invent facts, benchmarks, citations, or references. If real data is missing, explicitly label content as an assumption or a placeholder (e.g., ""TBD: measured latency"").
4) Include diagrams/tables as placeholders when helpful (e.g., ""Figure 1: Architecture diagram (TBD)"").
5) Maintain internal consistency: terminology, acronyms, units, and claims must align across sections.
6) Incorporate feedback from ALL reviewers explicitly: address each point and improve the draft accordingly.
7) When the content is ready for publication (after ALL reviewers send @@SIGNED OFF@@ in their reviews), output: FINAL: followed by the complete final content.";
    }

    private static string GetDefaultReviewerPrompt(int reviewerNumber)
    {
        return $@"You are DXO Reviewer {reviewerNumber}.

Review rubric (must address each area):
A) Technical correctness: identify incorrect claims, missing assumptions, and logic gaps.
B) Completeness: ensure all required sections exist and content is sufficiently detailed.
C) Clarity & structure: enforce clear flow, strong narrative, and unambiguous definitions.
D) Audience fit: ensure content matches the target audience's expertise level.

Output format:
1) Summary verdict (1 paragraph)
2) Major issues (bulleted, each with rationale + concrete fix)
3) Minor issues (bulleted)
4) Checklist (pass/fail items)

If and only if the draft is publication-ready from your perspective, include the token @@SIGNED OFF@@ on its own line at the end.";
    }
}

#region Request Models

public class CreateSessionRequest
{
    public string? Name { get; set; }
    public int? MaxIterations { get; set; }
    public string? StopMarker { get; set; }
    public bool? StopOnReviewerApproved { get; set; }
    public RunMode? RunMode { get; set; }
    
    /// <summary>
    /// The central topic for agents to discuss, independent of their root prompts.
    /// </summary>
    public string? Topic { get; set; }

    // Creator config
    public string? CreatorRootPrompt { get; set; }
    public string? CreatorModel { get; set; }
    public double? CreatorTemperature { get; set; }
    public int? CreatorMaxTokens { get; set; }
    public double? CreatorTopP { get; set; }
    public double? CreatorPresencePenalty { get; set; }
    public double? CreatorFrequencyPenalty { get; set; }

    /// <summary>
    /// Dynamic list of reviewers
    /// </summary>
    public List<ReviewerRequest>? Reviewers { get; set; }
}

/// <summary>
/// Request model for a single reviewer configuration
/// </summary>
public class ReviewerRequest
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? RootPrompt { get; set; }
    public string? Model { get; set; }
    public double? Temperature { get; set; }
    public int? MaxTokens { get; set; }
    public double? TopP { get; set; }
    public double? PresencePenalty { get; set; }
    public double? FrequencyPenalty { get; set; }
}

#endregion
