using System.ComponentModel.DataAnnotations;

namespace DXO.Models;

/// <summary>
/// Represents user-specific settings and preferences
/// </summary>
public class UserSettings
{
    /// <summary>
    /// User email (primary key) - matches auth system user identifier
    /// </summary>
    [Key]
    [MaxLength(200)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Optional reference to a custom Native Agent model configuration.
    /// If null, the system uses the default Native Agent from environment variables.
    /// If set, references a ConfiguredModel where IsNativeAgent is true.
    /// </summary>
    public int? NativeAgentModelId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
