using System.Security.Claims;
using Pulse.Web.Domain;

namespace Pulse.Web.Identity;

public static class RolePermissions
{
    public static void RequireAdministrator(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId) ||
            actorId == Guid.Empty ||
            !user.IsInRole(nameof(PulseRole.Administrator)))
        {
            throw new UnauthorizedAccessException("Administrator access is required.");
        }
    }
}
