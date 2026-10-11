using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pulse.Web.Data;
using Pulse.Web.Domain;
using System.Text.Json;
using System.Security.Claims;
using Pulse.Web.Identity;

namespace Pulse.Web.Services
{
    public sealed class ResultService(
    IDbContextFactory<PulseDbContext> dbFactory,
    TimeProvider timeProvider)
    {
        public async Task<ResultSnapshot> GetCurrentAsync(Guid questionId)
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var events = await db.Events
                .AsNoTracking()
                .Where(e => e.QuestionId == questionId)
                .OrderBy(e => e.Sequence)
                .ToListAsync();

            if (events.Count == 0)
                throw new InvalidOperationException("Question not found.");

            // Use the sequence number of the last event as the boundary for the result calculation
            var boundary = events[^1].Sequence;

            var version = ResultCalculator.CurrentVersion;

            Task<PresentedResult?> FindStoredAsync() =>
                db.PresentedResults
                    .AsNoTracking()
                    .SingleOrDefaultAsync(r =>
                        r.QuestionId == questionId &&
                        r.ThroughSequence == boundary &&
                        r.AlgorithmVersion == version);

            var existing = await FindStoredAsync();

            if (existing is not null)
                return ReadSnapshot(existing);

            var snapshot = ResultCalculator.Calculate(
                questionId, events, boundary, version);

            db.PresentedResults.Add(new PresentedResult
            {
                QuestionId = questionId,
                ThroughSequence = boundary,
                AlgorithmVersion = version,
                ResultJson = JsonSerializer.Serialize(snapshot),
                ServedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            });

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is SqliteException
                {
                    SqliteExtendedErrorCode: 2067
                })
            {
                // Another request may have stored this result first.
                existing = await FindStoredAsync();

                if (existing is null)
                    throw;

                return ReadSnapshot(existing);
            }

            return snapshot;
        }

        private static ResultSnapshot ReadSnapshot(PresentedResult stored)
        {
            if (stored.AlgorithmVersion is not ("v1" or "v2"))
                throw new NotSupportedException("Unsupported calculation version.");

            var snapshot =
                JsonSerializer.Deserialize<ResultSnapshot>(stored.ResultJson)
                ?? throw new InvalidOperationException("Invalid stored result.");

            if (snapshot.QuestionId != stored.QuestionId ||
                snapshot.ThroughSequence != stored.ThroughSequence ||
                snapshot.AlgorithmVersion != stored.AlgorithmVersion)
            {
                throw new InvalidOperationException(
                    "Stored result metadata does not match its contents.");
            }

            return snapshot;
        }

        public async Task<IReadOnlyList<ResultComparison>> CompareStoredAsync(ClaimsPrincipal user, Guid questionId)
        {
            RolePermissions.RequireAdministrator(user);
            await using var db = await dbFactory.CreateDbContextAsync();

            // Retrieve all stored results for the question, ordered by sequence number
            var storedResults = await db.PresentedResults
                .AsNoTracking()
                .Where(r => r.QuestionId == questionId)
                .OrderBy(r => r.ThroughSequence)
                .ToListAsync();

            if (storedResults.Count == 0)
                return Array.Empty<ResultComparison>();

            // last boundary is the sequence number of the last stored result, which is used to limit the events retrieved for comparison
            var lastBoundary = storedResults.Max(r => r.ThroughSequence);

            var events = await db.Events
                .AsNoTracking()
                .Where(e =>
                    e.QuestionId == questionId &&
                    e.Sequence <= lastBoundary)
                .OrderBy(e => e.Sequence)
                .ToListAsync();

            var comparisons = new List<ResultComparison>();

            foreach (var record in storedResults)
            {
                var stored = ReadSnapshot(record);

                var replayed = ResultCalculator.Calculate(
                    questionId,
                    events,
                    record.ThroughSequence,
                    record.AlgorithmVersion);

                comparisons.Add(new ResultComparison(
                    record.Id,
                    record.ServedAtUtc,
                    stored,
                    replayed,
                    SameResult(stored, replayed)));
            }

            return comparisons;
        }

        private static bool SameResult(ResultSnapshot first, ResultSnapshot second)
        {
            return first.QuestionId == second.QuestionId
                && first.ThroughSequence == second.ThroughSequence
                && first.AlgorithmVersion == second.AlgorithmVersion
                && first.ParticipantCount == second.ParticipantCount
                && first.ChangedAnswerCount == second.ChangedAnswerCount
                && first.ChangedAnswerPercentage == second.ChangedAnswerPercentage
                && first.TotalConfidence == second.TotalConfidence
                && first.Answers.SequenceEqual(second.Answers);
        }
    }
}
