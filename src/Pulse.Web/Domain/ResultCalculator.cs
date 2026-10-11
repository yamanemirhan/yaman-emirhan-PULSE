using System.Text.Json;

namespace Pulse.Web.Domain
{
    public static class ResultCalculator
    {
        public const string CurrentVersion = "v2";

        public static ResultSnapshot Calculate(
            Guid questionId,
            IEnumerable<PulseEvent> events,
            long throughSequence,
            string algorithmVersion = CurrentVersion)
        {
            if (algorithmVersion is not ("v1" or "v2"))
                throw new NotSupportedException("Unsupported calculation version.");

            var histories = events
                .Where(e =>
                    e.QuestionId == questionId &&
                    e.Kind == PulseEventKind.VoteCast &&
                    e.Sequence <= throughSequence)
                .OrderBy(e => e.Sequence)
                .Select(e => new
                {
                    e.ActorId,
                    Vote = ReadVote(e)
                })
                .GroupBy(e => e.ActorId)
                .Select(group => group.Select(e => e.Vote).ToList())
                .ToList();

            var latestVotes = histories
                .Select(history => history.Last())
                .ToList();

            var participantCount = latestVotes.Count;
            var totalConfidence = latestVotes.Sum(v => (long)v.Confidence);

            var changedCount = histories.Count(history =>
                history.Select(v => v.Answer).Distinct().Count() > 1);

            var summaries = Enum.GetValues<VoteAnswer>()
                .Select(answer =>
                {
                    var group = latestVotes
                        .Where(v => v.Answer == answer)
                        .ToList();

                    decimal? average = group.Count == 0
                        ? null
                        : Round(group.Average(v => (decimal)v.Confidence));

                    return new AnswerSummary(
                        answer,
                        group.Count,
                        Percentage(group.Count, participantCount),
                        average,
                        algorithmVersion == "v2" && totalConfidence > 0
                            ? Round(100m * group.Sum(v => (long)v.Confidence) / totalConfidence)
                            : null);
                })
                .ToArray();

            return new ResultSnapshot(
                questionId,
                throughSequence,
                algorithmVersion,
                participantCount,
                changedCount,
                Percentage(changedCount, participantCount),
                summaries,
                algorithmVersion == "v2" ? totalConfidence : null);
        }

        private static VoteCastPayload ReadVote(PulseEvent pulseEvent)
        {
            if (pulseEvent.PayloadVersion != 1)
                throw new NotSupportedException("Unsupported vote payload.");

            var vote =
                JsonSerializer.Deserialize<VoteCastPayload>(pulseEvent.PayloadJson)
                ?? throw new InvalidOperationException("Invalid vote payload.");

            if (!Enum.IsDefined(vote.Answer) || vote.Confidence is < 0 or > 100)
                throw new InvalidOperationException("Invalid stored vote.");

            return vote;
        }

        private static decimal? Percentage(int count, int total) =>
            total == 0 ? null : Round(100m * count / total);

        private static decimal Round(decimal value) =>
            Math.Round(value, 1, MidpointRounding.AwayFromZero);
    }
}
