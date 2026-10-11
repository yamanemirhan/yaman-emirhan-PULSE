using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Pulse.Web.Data;
using Pulse.Web.Domain;

namespace Pulse.Web.Services;

public sealed class TimelineService(IDbContextFactory<PulseDbContext> dbFactory)
{
    public async Task<IReadOnlyList<TimelineEntry>> GetAsync(ClaimsPrincipal user, Guid questionId)
    {
        if (user.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var viewerId) || viewerId == Guid.Empty ||
            !Enum.TryParse<PulseRole>(user.FindFirstValue(ClaimTypes.Role), out var role) || !Enum.IsDefined(role))
            throw new UnauthorizedAccessException();

        await using var db = await dbFactory.CreateDbContextAsync();
        var events = await db.Events.AsNoTracking().Where(e => e.QuestionId == questionId)
            .OrderBy(e => e.Sequence).ToListAsync();
        // Return a filtered view, never raw actor IDs or hidden evidence payloads.
        return QuestionTimeline.Build(questionId, events, viewerId, role);
    }
}
