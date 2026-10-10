using Microsoft.EntityFrameworkCore;
using Pulse.Web.Data;
using Pulse.Web.Domain;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;

namespace Pulse.Web.Services
{
    public sealed record QuestionDetails(Guid Id, string Text, DateTime CreatedAtUtc);

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

            if (created.PayloadVersion != 1)
                throw new NotSupportedException("Unsupported question payload.");

            var payload =
                JsonSerializer.Deserialize<QuestionCreatedPayload>(
                    created.PayloadJson)
                ?? throw new InvalidOperationException("Invalid question payload.");

            return new QuestionDetails(
                questionId,
                payload.Text,
                created.RecordedAtUtc);
        }
    }
}
