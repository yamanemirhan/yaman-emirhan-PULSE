using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Pulse.Web.Domain;

namespace Pulse.Web.Identity;

public sealed record DemoRoleSwitching(bool Enabled)
{
    public const string DemoClaim = "pulse:demo-role";

    public static ClaimsPrincipal CreatePrincipal(Guid actorId, PulseRole role)
        => new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, actorId.ToString()),
            new Claim(ClaimTypes.Role, role.ToString()),
            new Claim(DemoClaim, "true")
        }, ParticipantIdentityMiddleware.Scheme));

    public static async Task<IResult> SwitchAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (context.User.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId) ||
            actorId == Guid.Empty)
            return Results.Unauthorized();

        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest("Invalid form token. Reload the page and try again.");
        }

        var form = await context.Request.ReadFormAsync();
        var roleName = form["role"].ToString();
        if (!Enum.TryParse<PulseRole>(roleName, out var role) ||
            !Enum.IsDefined(role) || role.ToString() != roleName)
            return Results.BadRequest("Unknown role.");

        var returnUrl = form["returnUrl"].ToString();
        if (!IsLocalPath(returnUrl))
            return Results.BadRequest("Invalid return path.");

        // Keep the participant identity: switching roles must not grant another vote.
        await context.SignInAsync(ParticipantIdentityMiddleware.Scheme,
            CreatePrincipal(actorId, role),
            new AuthenticationProperties { IsPersistent = true });

        // A normal form POST and full reload recreate the Blazor circuit with new claims.
        return Results.LocalRedirect(returnUrl);
    }

    private static bool IsLocalPath(string path) =>
        path.StartsWith('/') && !path.StartsWith("//") &&
        !path.Contains('\\') && !path.Any(char.IsControl);
}
