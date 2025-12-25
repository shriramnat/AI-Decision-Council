using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXO.Models;

/// <summary>
/// Represents a message in the orchestration conversation
/// </summary>
public class Message
{
    [Key]
    public Guid MessageId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid SessionId { get; set; }

    /// <summary>
    /// The persona that generated this message (Creator, Reviewer, or System)
    /// </summary>
    public Persona Persona { get; set; }

    /// <summary>
    /// The role of this message in the API conversation (system, user, assistant)
    /// </summary>
    public MessageRole Role { get; set; }

    /// <summary>
    /// The content of the message
    /// </summary>
    [Required]
    [Column(TypeName = "TEXT")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// The iteration number when this message was created
    /// </summary>
    public int Iteration { get; set; }

    /// <summary>
    /// Optional: Model used to generate this message
    /// </summary>
    [MaxLength(100)]
    public string? ModelUsed { get; set; }

    /// <summary>
    /// Optional: Reviewer ID for dynamic reviewers
    /// </summary>
    [MaxLength(100)]
    public string? ReviewerId { get; set; }

    /// <summary>
    /// Optional: Reviewer display name for dynamic reviewers
    /// </summary>
    [MaxLength(200)]
    public string? ReviewerName { get; set; }

    /// <summary>
    /// Token usage information (JSON serialized)
    /// </summary>
    [Column(TypeName = "TEXT")]
    public string? TokenUsageJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation property for the parent session
    /// </summary>
    [ForeignKey(nameof(SessionId))]
    public virtual Session? Session { get; set; }
}
