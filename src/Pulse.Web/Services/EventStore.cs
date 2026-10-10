using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pulse.Web.Data;
using Pulse.Web.Domain;

namespace Pulse.Web.Services
{
    public sealed record AppendResult(long Sequence, bool IsDuplicate);

    public sealed class EventStore(IDbContextFactory<PulseDbContext> dbFactory, TimeProvider timeProvider)
    {
        public async Task<AppendResult> AppendAsync(NewEvent draft, CancellationToken cancellationToken = default)
        {
            await using var db =
                await dbFactory.CreateDbContextAsync(cancellationToken);

            await db.Database.OpenConnectionAsync(cancellationToken);

            var connection =
                (SqliteConnection)db.Database.GetDbConnection();

            await using var transaction =
                connection.BeginTransaction(deferred: false);

            await db.Database.UseTransactionAsync(
                transaction, cancellationToken);

            var existing = await db.Events
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    e => e.ActorId == draft.ActorId
                      && e.QuestionId == draft.QuestionId
                      && e.RequestId == draft.RequestId,
                    cancellationToken);

            // if an event with the same actor, question, and request ID already exists
            if (existing is not null)
            {
                var sameContent =
                    existing.Kind == draft.Kind &&
                    existing.PayloadVersion == draft.PayloadVersion &&
                    existing.PayloadJson == draft.PayloadJson;

                if (!sameContent)
                {
                    throw new InvalidOperationException(
                        "The request ID was already used with different content.");
                }

                return new AppendResult(existing.Sequence, IsDuplicate: true);
            }

            // get the hash of the previous event, or use the genesis hash if there are no previous events  
            var previousHash = await db.Events
                .OrderByDescending(e => e.Sequence)
                .Select(e => e.Hash)
                .FirstOrDefaultAsync(cancellationToken)
                ?? EventHasher.GenesisHash;

            var pulseEvent = new PulseEvent
            {
                QuestionId = draft.QuestionId,
                Kind = draft.Kind,
                ActorId = draft.ActorId,
                ActorRole = draft.ActorRole,
                RecordedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
                RequestId = draft.RequestId,
                PayloadVersion = draft.PayloadVersion,
                PayloadJson = draft.PayloadJson
            };

            db.Events.Add(pulseEvent);
            await db.SaveChangesAsync(cancellationToken);

            var hash = EventHasher.Compute(pulseEvent, previousHash);

            var affectedRows = await db.Database.ExecuteSqlAsync(
                $"""
            UPDATE Events
            SET PreviousHash = {previousHash}, Hash = {hash}
            WHERE Sequence = {pulseEvent.Sequence} AND Hash = ''
            """,
                cancellationToken);

            // If the update did not affect exactly one row, it means the hash could not be completed
            if (affectedRows != 1)
            {
                throw new InvalidOperationException(
                    "The new event's hash could not be completed.");
            }

            await transaction.CommitAsync(cancellationToken);

            return new AppendResult(pulseEvent.Sequence, IsDuplicate: false);
        }
    }
}
