using Microsoft.EntityFrameworkCore;
using Pulse.Web.Data;
using Pulse.Web.Domain;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;

namespace Pulse.Web.Services
{
    public sealed record QuestionDetails(Guid Id, string Text, DateTime CreatedAtUtc);
    public sealed record QuestionListPage(IReadOnlyList<QuestionDetails> Items, long? NextBeforeSequence);

    public sealed class QuestionService(EventStore eventStore, IDbContextFactory<PulseDbContext> dbFactory)
    {
        public async Task<Guid> CreateAsync(ClaimsPrincipal user, Guid questionId, Guid requestId, string text)
        {
            if (user.Identity?.IsAuthenticated != true ||
                !Guid.TryParse(
                    user.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                    out var actorId) ||
                actorId == Guid.Empty ||
                !Enum.TryParse<PulseRole>(
                    user.FindFirst(ClaimTypes.Role)?.Value,
                    out var role) ||
                !Enum.IsDefined(role))
            {
                throw new UnauthorizedAccessException();
            }

            text = (text ?? string.Empty).Trim();

            if (text.Length is < 1 or > 500)
            {
                throw new ValidationException(
                    "Enter a question between 1 and 500 characters.");
            }

            if (questionId == Guid.Empty || requestId == Guid.Empty)
            {
                throw new ValidationException("Invalid request.");
            }

            var payload = JsonSerializer.Serialize(
                new QuestionCreatedPayload(text));

            // Append the QuestionCreated event to the event store
            await eventStore.AppendAsync(new NewEvent(
                questionId,
                PulseEventKind.QuestionCreated,
                actorId,
                role,
                requestId,
                payload));

            return questionId;
        }

        public async Task<QuestionDetails?> GetAsync(Guid questionId)
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var created = await db.Events
                .AsNoTracking()
                .SingleOrDefaultAsync(e =>
                    e.QuestionId == questionId &&
                    e.Kind == PulseEventKind.QuestionCreated);

            if (created is null)
                return null;

            return ReadDetails(created);
        }

        public async Task<QuestionListPage> ListAsync(
            ClaimsPrincipal user, long? beforeSequence = null)
        {
            if (user.Identity?.IsAuthenticated != true ||
                !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId) ||
                actorId == Guid.Empty ||
                !Enum.TryParse<PulseRole>(user.FindFirstValue(ClaimTypes.Role), out var role) ||
                !Enum.IsDefined(role))
                throw new UnauthorizedAccessException();

            if (beforeSequence is <= 0)
                throw new ValidationException("Invalid question list page.");

            await using var db = await dbFactory.CreateDbContextAsync();
            var query = db.Events.AsNoTracking()
                .Where(e => e.Kind == PulseEventKind.QuestionCreated);

            // Scope the database query itself; never fetch other people's questions
            // and rely on the browser to hide them.
            if (role == PulseRole.Participant)
                query = query.Where(e => e.ActorId == actorId);

            if (beforeSequence is { } boundary)
                query = query.Where(e => e.Sequence < boundary);

            const int pageSize = 20;
            var events = await query.OrderByDescending(e => e.Sequence)
                .Take(pageSize + 1)
                .ToListAsync();

            var hasMore = events.Count > pageSize;
            var page = events.Take(pageSize).ToList();
            return new QuestionListPage(
                page.Select(ReadDetails).ToArray(),
                hasMore ? page[^1].Sequence : null);
        }

        private static QuestionDetails ReadDetails(PulseEvent created)
        {
            if (created.PayloadVersion != 1)
                throw new NotSupportedException("Unsupported question payload.");

            var payload =
                JsonSerializer.Deserialize<QuestionCreatedPayload>(
                    created.PayloadJson)
                ?? throw new InvalidOperationException("Invalid question payload.");

            return new QuestionDetails(
                created.QuestionId,
                payload.Text,
                created.RecordedAtUtc);
        }
    }
}
