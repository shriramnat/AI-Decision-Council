namespace DXO.Models;

/// <summary>
/// Represents the persona type in the orchestration loop
/// </summary>
public enum Persona
{
    System,
    Creator,
    Reviewer1,
    Reviewer2
}

/// <summary>
/// Represents the role of a message in the conversation
/// </summary>
public enum MessageRole
{
    System,
    User,
    Assistant
}

/// <summary>
/// Represents the status of a session
/// </summary>
public enum SessionStatus
{
    Created,
    Running,
    Paused,
    Completed,
    Stopped,
    Error
}

/// <summary>
/// Represents the reason why a session stopped
/// </summary>
public enum StopReason
{
    None,
    FinalMarkerDetected,
    UserStopped,
    MaxIterationsReached,
    ReviewerApproved,
    Error
}

/// <summary>
/// Run mode for the orchestration loop
/// </summary>
public enum RunMode
{
    Auto,
    Step
}
