using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pulse.Web.Data;
using Pulse.Web.Domain;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;

namespace Pulse.Web.Services
{
    public sealed record AppendResult(long Sequence, bool IsDuplicate, bool IsUnchanged = false);

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

            // Compare against the latest vote while holding SQLite's write lock.
            // A UI-only check would allow two concurrent submissions to append the same vote.
            if (draft.Kind == PulseEventKind.VoteCast)
            {
                var incomingVote = ReadVote(draft.PayloadVersion, draft.PayloadJson);
                var latestVote = await db.Events
                    .AsNoTracking()
                    .Where(e => e.QuestionId == draft.QuestionId &&
                                e.ActorId == draft.ActorId &&
                                e.Kind == PulseEventKind.VoteCast)
                    .OrderByDescending(e => e.Sequence)
                    .FirstOrDefaultAsync(cancellationToken);

                if (latestVote is not null &&
                    ReadVote(latestVote.PayloadVersion, latestVote.PayloadJson) == incomingVote)
                {
                    return new AppendResult(latestVote.Sequence, IsDuplicate: false, IsUnchanged: true);
                }
            }

            if (draft.Kind is PulseEventKind.EvidenceSubmitted or PulseEventKind.EvidenceReviewed)
            {
                // Review preconditions must be checked under the same write lock as the append.
                var history = await db.Events.AsNoTracking()
                    .Where(e => e.QuestionId == draft.QuestionId &&
                        (e.Kind == PulseEventKind.EvidenceSubmitted || e.Kind == PulseEventKind.EvidenceReviewed))
                    .OrderBy(e => e.Sequence).ToListAsync(cancellationToken);
                var evidence = EvidenceHistory.Replay(draft.QuestionId, history);

                if (draft.Kind == PulseEventKind.EvidenceSubmitted)
                {
                    var payload = EvidenceHistory.Read<EvidenceSubmittedPayload>(draft.PayloadVersion, draft.PayloadJson);
                    EvidenceHistory.Validate(payload);
                    if (evidence.Any(e => e.Id == payload.EvidenceId))
                        throw new ValidationException("This evidence ID has already been submitted.");
                }
                else
                {
                    if (draft.ActorRole != PulseRole.Moderator)
                        throw new UnauthorizedAccessException("Only Moderator can review evidence.");
                    var payload = EvidenceHistory.Read<EvidenceReviewedPayload>(draft.PayloadVersion, draft.PayloadJson);
                    EvidenceHistory.Validate(payload);
                    var current = evidence.SingleOrDefault(e => e.Id == payload.EvidenceId)
                        ?? throw new ValidationException("Evidence not found for this question.");
                    if (current.SubmittedBy == draft.ActorId)
                        throw new ValidationException("You cannot review your own evidence. Another Moderator must review it.");
                    if (current.LastChangeSequence != payload.ExpectedSequence)
                        throw new ValidationException("This evidence was reviewed by another request. Refresh evidence before deciding again.");
                    if (current.Status == payload.Status && current.ReviewReason == payload.Reason)
                        return new AppendResult(current.LastChangeSequence, IsDuplicate: false, IsUnchanged: true);
                }
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

        private static VoteCastPayload ReadVote(int version, string json)
        {
            if (version != 1)
                throw new NotSupportedException("Unsupported vote payload.");

            var vote = JsonSerializer.Deserialize<VoteCastPayload>(json)
                ?? throw new InvalidOperationException("Invalid vote payload.");

            if (!Enum.IsDefined(vote.Answer) || vote.Confidence is < 0 or > 100)
                throw new InvalidOperationException("Invalid vote payload.");

            return vote;
        }
    }
}
