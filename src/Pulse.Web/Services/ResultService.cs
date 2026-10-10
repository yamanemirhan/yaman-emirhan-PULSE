using Microsoft.EntityFrameworkCore;
using Pulse.Web.Data;
using Pulse.Web.Domain;

namespace Pulse.Web.Services
{
    public sealed class ResultService(IDbContextFactory<PulseDbContext> dbFactory)
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

            return ResultCalculator.Calculate(questionId, events, boundary);
        }
    }
}
