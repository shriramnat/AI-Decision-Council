using Microsoft.AspNetCore.Authorization;

namespace DXO.Services.Authorization;

/// <summary>
/// Authorization requirement for approved user list.
/// </summary>
public class ApprovedListRequirement : IAuthorizationRequirement
{
    public ApprovedListRequirement()
    {
    }
}
