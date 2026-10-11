using Microsoft.EntityFrameworkCore;
using Pulse.Web.Data;
using Pulse.Web.Domain;

namespace Pulse.Web.Services
{
    public sealed class EventChainVerifier(IDbContextFactory<PulseDbContext> dbFactory)
    {
        public async Task<EventChainVerification> VerifyAsync(CancellationToken cancellationToken = default)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var previousHash = EventHasher.GenesisHash;
            long checkedEvents = 0;
            long? lastVerifiedSequence = null;

            // One ordered read gives a consistent SQLite snapshot of the global chain.
            // Sequence gaps are allowed: continuity is checked through PreviousHash.
            await foreach (var pulseEvent in db.Events
                .AsNoTracking()
                .OrderBy(e => e.Sequence)
                .AsAsyncEnumerable()
                .WithCancellation(cancellationToken))
            {
                checkedEvents++;
                string? problem = null;

                if (pulseEvent.HashVersion != 1)
                {
                    problem = "This event uses an unsupported hash version; verification stopped.";
                }
                else if (!string.Equals(pulseEvent.PreviousHash, previousHash, StringComparison.Ordinal))
                {
                    problem = "The previous hash does not match the preceding event.";
                }
                else if (!string.Equals(
                    pulseEvent.Hash,
                    EventHasher.Compute(pulseEvent, previousHash),
                    StringComparison.Ordinal))
                {
                    problem = "The stored hash does not match the event's current contents.";
                }

                if (problem is not null)
                {
                    return new EventChainVerification(
                        checkedEvents, lastVerifiedSequence, pulseEvent.Sequence, problem);
                }

                previousHash = pulseEvent.Hash;
                lastVerifiedSequence = pulseEvent.Sequence;
            }

            return new EventChainVerification(checkedEvents, lastVerifiedSequence, null, null);
        }
    }

}