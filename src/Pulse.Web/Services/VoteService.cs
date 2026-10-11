using Microsoft.EntityFrameworkCore;
using Pulse.Web.Data;
using Pulse.Web.Domain;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;

namespace Pulse.Web.Services
{
    public sealed class VoteService(EventStore eventStore, QuestionService questions, IDbContextFactory<PulseDbContext> dbFactory)
    {
        public async Task<AppendResult> CastAsync(
            ClaimsPrincipal user,
            Guid questionId,
            Guid requestId,
            VoteAnswer answer,
            int confidence)
        {
            var actor = ReadActor(user);

            if (!Enum.IsDefined(answer) || confidence is < 0 or > 100)
                throw new ValidationException("Choose a valid answer and confidence.");

            if (questionId == Guid.Empty || requestId == Guid.Empty)
                throw new ValidationException("Invalid request.");

            if (await questions.GetAsync(questionId) is null)
                throw new ValidationException("Question not found.");

            var payload = JsonSerializer.Serialize(
                new VoteCastPayload(answer, confidence));

            return await eventStore.AppendAsync(new NewEvent(
                questionId,
                PulseEventKind.VoteCast,
                actor.Id,
                actor.Role,
                requestId,
                payload));
        }

        public async Task<VoteCastPayload?> GetLatestAsync(
            ClaimsPrincipal user,
            Guid questionId)
        {
            var actor = ReadActor(user);

            await using var db = await dbFactory.CreateDbContextAsync();

            var latest = await db.Events
                .AsNoTracking()
                .Where(e =>
                    e.QuestionId == questionId &&
                    e.ActorId == actor.Id &&
                    e.Kind == PulseEventKind.VoteCast)
                .OrderByDescending(e => e.Sequence)
                .FirstOrDefaultAsync();

            if (latest is null)
                return null;

            if (latest.PayloadVersion != 1)
                throw new NotSupportedException("Unsupported vote payload.");

            return JsonSerializer.Deserialize<VoteCastPayload>(latest.PayloadJson)
                ?? throw new InvalidOperationException("Invalid vote payload.");
        }

        private static (Guid Id, PulseRole Role) ReadActor(ClaimsPrincipal user)
        {
            if (user.Identity?.IsAuthenticated != true ||
                !Guid.TryParse(
                    user.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                    out var id) ||
                id == Guid.Empty ||
                !Enum.TryParse<PulseRole>(
                    user.FindFirst(ClaimTypes.Role)?.Value,
                    out var role) ||
                !Enum.IsDefined(role))
            {
                throw new UnauthorizedAccessException();
            }

            return (id, role);
        }
    }
}
