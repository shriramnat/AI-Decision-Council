using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXO.Models;

/// <summary>
/// Represents a feedback round in a session for audit friendliness
/// Tracks each iteration's output and user feedback
/// </summary>
public class FeedbackRound
{
    [Key]
    public Guid FeedbackRoundId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid SessionId { get; set; }

    /// <summary>
    /// The iteration number for this feedback round
    /// </summary>
    public int Iteration { get; set; }

    /// <summary>
    /// The draft content produced by the Creator at this iteration
    /// </summary>
    [Column(TypeName = "TEXT")]
    public string? DraftContent { get; set; }

    /// <summary>
    /// User feedback provided for this iteration
    /// </summary>
    [Column(TypeName = "TEXT")]
    public string? UserFeedback { get; set; }

    /// <summary>
    /// Whether all reviewers approved at this iteration
    /// </summary>
    public bool AllReviewersApproved { get; set; } = false;

    /// <summary>
    /// JSON serialized reviewer feedback summaries
    /// </summary>
    [Column(TypeName = "TEXT")]
    public string? ReviewerFeedbackJson { get; set; }

    /// <summary>
    /// Timestamp when this feedback round was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when user feedback was provided
    /// </summary>
    public DateTime? UserFeedbackAt { get; set; }

    /// <summary>
    /// Navigation property for the parent session
    /// </summary>
    [ForeignKey(nameof(SessionId))]
    public virtual Session? Session { get; set; }
}

/// <summary>
/// Represents a reviewer's feedback for a specific round
/// </summary>
public class ReviewerFeedbackSummary
{
    public string ReviewerId { get; set; } = string.Empty;
    public string ReviewerName { get; set; } = string.Empty;
    public string Feedback { get; set; } = string.Empty;
    public bool Approved { get; set; } = false;
}
