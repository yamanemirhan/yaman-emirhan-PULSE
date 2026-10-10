using Microsoft.AspNetCore.Authentication;
using Pulse.Web.Domain;
using System.Security.Claims;

namespace Pulse.Web.Identity
{
    public sealed class ParticipantIdentityMiddleware(RequestDelegate next)
    {
        public const string Scheme = "Pulse.Participant";

        public async Task InvokeAsync(HttpContext context)
        {
            var isPageRequest =
                HttpMethods.IsGet(context.Request.Method) &&
                context.Request.Headers.Accept.ToString()
                    .Contains("text/html", StringComparison.OrdinalIgnoreCase);

            if (isPageRequest &&
                context.User.Identity?.IsAuthenticated != true)
            {
                var claims = new[]
                {
                new Claim(
                    ClaimTypes.NameIdentifier,
                    Guid.NewGuid().ToString()),

                new Claim(
                    ClaimTypes.Role,
                    PulseRole.Participant.ToString())
            };

                var identity = new ClaimsIdentity(claims, Scheme);
                var principal = new ClaimsPrincipal(identity);

                await context.SignInAsync(
                    Scheme,
                    principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = true
                    });

                context.User = principal;
            }

            await next(context);
        }
    }
}
