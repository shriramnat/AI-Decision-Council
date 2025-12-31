using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXO.Models;

/// <summary>
/// Represents a DXO orchestration session
/// </summary>
public class Session
{
    [Key]
    public Guid SessionId { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = "New Session";

    public SessionStatus Status { get; set; } = SessionStatus.Created;

    public StopReason StopReason { get; set; } = StopReason.None;

    public int MaxIterations { get; set; } = 8;

    public int CurrentIteration { get; set; } = 0;

    /// <summary>
    /// Tracks the feedback version (v1, v2, v3, etc.). 
    /// Increments each time user submits feedback to iterate on completed output.
    /// </summary>
    public int FeedbackVersion { get; set; } = 1;

    [MaxLength(50)]
    public string StopMarker { get; set; } = "FINAL:";

    public bool StopOnReviewerApproved { get; set; } = false;

    public RunMode RunMode { get; set; } = RunMode.Auto;

    /// <summary>
    /// The central topic that agents discuss, independent of their root prompts.
    /// This defines what the agents should focus their discussion on.
    /// </summary>
    [Column(TypeName = "TEXT")]
    public string? Topic { get; set; }

    /// <summary>
    /// The final content extracted after stop marker or last draft
    /// </summary>
    [Column(TypeName = "TEXT")]
    public string? FinalContent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// JSON serialized creator persona configuration
    /// </summary>
    [Column(TypeName = "TEXT")]
    public string CreatorConfigJson { get; set; } = "{}";

    /// <summary>
    /// JSON serialized array of reviewer configurations
    /// </summary>
    [Column(TypeName = "TEXT")]
    public string ReviewersConfigJson { get; set; } = "[]";

    /// <summary>
    /// Navigation property for messages in this session
    /// </summary>
    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();

    /// <summary>
    /// Navigation property for feedback rounds in this session
    /// </summary>
    public virtual ICollection<FeedbackRound> FeedbackRounds { get; set; } = new List<FeedbackRound>();
}

/// <summary>
/// Configuration for a persona (Creator or Reviewer)
/// </summary>
public class PersonaConfig
{
    public string RootPrompt { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o";
    public double Temperature { get; set; } = 0.7;
    public int MaxOutputTokens { get; set; } = 4096;
    public double TopP { get; set; } = 1.0;
    public double PresencePenalty { get; set; } = 0.0;
    public double FrequencyPenalty { get; set; } = 0.0;
}

/// <summary>
/// Configuration for a reviewer with an identifier and name
/// </summary>
public class ReviewerConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RootPrompt { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o";
    public double Temperature { get; set; } = 0.5;
    public int MaxOutputTokens { get; set; } = 4096;
    public double TopP { get; set; } = 1.0;
    public double PresencePenalty { get; set; } = 0.0;
    public double FrequencyPenalty { get; set; } = 0.0;
}