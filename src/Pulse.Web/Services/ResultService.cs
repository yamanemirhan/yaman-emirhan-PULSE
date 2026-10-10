using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pulse.Web.Data;
using Pulse.Web.Domain;
using System.Text.Json;

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
            if (stored.AlgorithmVersion != ResultCalculator.CurrentVersion)
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
    }
}