namespace DXO.Models;

/// <summary>
/// Data Transfer Object for Session responses
/// Avoids circular references by excluding navigation properties
/// </summary>
public class SessionDto
{
    public Guid SessionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public SessionStatus Status { get; set; }
    public StopReason StopReason { get; set; }
    public int MaxIterations { get; set; }
    public int CurrentIteration { get; set; }
    public int FeedbackVersion { get; set; }
    public string StopMarker { get; set; } = string.Empty;
    public bool StopOnReviewerApproved { get; set; }
    public RunMode RunMode { get; set; }
    public string? Topic { get; set; }
    public string? FinalContent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string CreatorConfigJson { get; set; } = string.Empty;
    public string ReviewersConfigJson { get; set; } = string.Empty;

    /// <summary>
    /// Creates a SessionDto from a Session entity
    /// </summary>
    public static SessionDto FromSession(Session session)
    {
        return new SessionDto
        {
            SessionId = session.SessionId,
            Name = session.Name,
            Status = session.Status,
            StopReason = session.StopReason,
            MaxIterations = session.MaxIterations,
            CurrentIteration = session.CurrentIteration,
            FeedbackVersion = session.FeedbackVersion,
            StopMarker = session.StopMarker,
            StopOnReviewerApproved = session.StopOnReviewerApproved,
            RunMode = session.RunMode,
            Topic = session.Topic,
            FinalContent = session.FinalContent,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt,
            CreatorConfigJson = session.CreatorConfigJson,
            ReviewersConfigJson = session.ReviewersConfigJson
        };
    }
}