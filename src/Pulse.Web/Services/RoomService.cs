using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pulse.Web.Data;
using Pulse.Web.Domain;
using Pulse.Web.Identity;

namespace Pulse.Web.Services;

public sealed record RoomState(long FeatureSequence, Guid QuestionId, string QuestionText,
    DateTime FeaturedAtUtc, long QuestionSequence);

public sealed class RoomService(EventStore eventStore, IDbContextFactory<PulseDbContext> dbFactory)
{
    public async Task<RoomState?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        // One SQL read keeps the featured question and its event boundary consistent.
        var current = await db.Events.AsNoTracking()
            .Where(e => e.Kind == PulseEventKind.QuestionFeatured)
            .OrderByDescending(e => e.Sequence)
            .Select(e => new
            {
                e.Sequence, e.QuestionId, e.RecordedAtUtc,
                Creation = db.Events.Where(q => q.QuestionId == e.QuestionId && q.Kind == PulseEventKind.QuestionCreated)
                    .Select(q => new { q.PayloadVersion, q.PayloadJson }).FirstOrDefault(),
                QuestionSequence = db.Events.Where(q => q.QuestionId == e.QuestionId).Max(q => q.Sequence)
            }).FirstOrDefaultAsync(cancellationToken);
        if (current is null) return null;
        if (current.Creation is null || current.Creation.PayloadVersion != 1)
            throw new InvalidOperationException("The featured question cannot be read.");
        var question = JsonSerializer.Deserialize<QuestionCreatedPayload>(current.Creation.PayloadJson)
            ?? throw new InvalidOperationException("Invalid question payload.");
        return new(current.Sequence, current.QuestionId, question.Text, current.RecordedAtUtc, current.QuestionSequence);
    }

    public Task<AppendResult> FeatureAsync(ClaimsPrincipal user, Guid questionId, Guid requestId, long expectedFeatureSequence)
    {
        RolePermissions.RequireAdministrator(user);
        if (questionId == Guid.Empty || requestId == Guid.Empty || expectedFeatureSequence < 0)
            throw new ValidationException("Invalid room update.");
        return eventStore.AppendAsync(new NewEvent(questionId, PulseEventKind.QuestionFeatured,
            Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!), PulseRole.Administrator, requestId,
            JsonSerializer.Serialize(new QuestionFeaturedPayload(expectedFeatureSequence))));
    }
}
