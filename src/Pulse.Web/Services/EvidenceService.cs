using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pulse.Web.Data;
using Pulse.Web.Domain;

namespace Pulse.Web.Services;

public sealed class EvidenceService(
    EventStore eventStore, QuestionService questions, IDbContextFactory<PulseDbContext> dbFactory)
{
    public async Task<AppendResult> SubmitAsync(ClaimsPrincipal user, Guid questionId,
        Guid evidenceId, Guid requestId, string title, string url, string summary)
    {
        var actor = ReadActor(user);
        ValidateRequest(questionId, requestId);
        var payload = new EvidenceSubmittedPayload(evidenceId,
            (title ?? "").Trim(), (url ?? "").Trim(), (summary ?? "").Trim());
        EvidenceHistory.Validate(payload);
        if (await questions.GetAsync(questionId) is null)
            throw new ValidationException("Question not found.");

        return await eventStore.AppendAsync(new NewEvent(questionId,
            PulseEventKind.EvidenceSubmitted, actor.Id, actor.Role, requestId,
            JsonSerializer.Serialize(payload)));
    }

    public async Task<AppendResult> ReviewAsync(ClaimsPrincipal user, Guid questionId,
        Guid evidenceId, Guid requestId, long expectedSequence, EvidenceStatus status, string reason)
    {
        var actor = ReadActor(user);
        if (actor.Role != PulseRole.Moderator)
            throw new UnauthorizedAccessException("Only Moderator can review evidence.");
        ValidateRequest(questionId, requestId);
        var payload = new EvidenceReviewedPayload(evidenceId, status, (reason ?? "").Trim(), expectedSequence);
        EvidenceHistory.Validate(payload);
        return await eventStore.AppendAsync(new NewEvent(questionId,
            PulseEventKind.EvidenceReviewed, actor.Id, actor.Role, requestId,
            JsonSerializer.Serialize(payload)));
    }

    public async Task<IReadOnlyList<EvidenceDetails>> ListAsync(
        ClaimsPrincipal user, Guid questionId, long throughSequence = long.MaxValue)
    {
        var actor = ReadActor(user);
        if (throughSequence < 0)
            throw new ValidationException("Invalid history boundary.");

        await using var db = await dbFactory.CreateDbContextAsync();
        var events = await db.Events.AsNoTracking()
            .Where(e => e.QuestionId == questionId && e.Sequence <= throughSequence &&
                (e.Kind == PulseEventKind.EvidenceSubmitted || e.Kind == PulseEventKind.EvidenceReviewed))
            .OrderBy(e => e.Sequence).ToListAsync();

        // Visibility is derived at the requested boundary, not from today's review status.
        return EvidenceHistory.Replay(questionId, events, throughSequence)
            .Where(e => e.Status == EvidenceStatus.Approved || e.SubmittedBy == actor.Id ||
                actor.Role is PulseRole.Moderator or PulseRole.Administrator)
            .ToArray();
    }

    private static void ValidateRequest(Guid questionId, Guid requestId)
    {
        if (questionId == Guid.Empty || requestId == Guid.Empty)
            throw new ValidationException("Invalid request.");
    }

    private static (Guid Id, PulseRole Role) ReadActor(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || id == Guid.Empty ||
            !Enum.TryParse<PulseRole>(user.FindFirstValue(ClaimTypes.Role), out var role) || !Enum.IsDefined(role))
            throw new UnauthorizedAccessException();
        return (id, role);
    }
}
