namespace DXO.Services.Authorization;

/// <summary>
/// Service for managing approved user list authorization.
/// Designed to support both file-based and database-based implementations.
/// </summary>
public interface IApprovedListService
{
    /// <summary>
    /// Checks if a user email is approved for access.
    /// </summary>
    /// <param name="email">The user's email address (will be normalized internally)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if the user is approved or wildcard is enabled, false otherwise</returns>
    Task<bool> IsUserApprovedAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Checks if wildcard access is enabled (all authenticated users allowed).
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if wildcard "*" is in the approved list</returns>
    Task<bool> IsWildcardEnabledAsync(CancellationToken ct = default);

    /// <summary>
    /// Reloads the approved list from the data source.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    Task ReloadAsync(CancellationToken ct = default);
}
