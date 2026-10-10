namespace Pulse.Web.Domain
{
    public sealed class PresentedResult
    {
        public Guid Id { get; init; } = Guid.NewGuid();

        public required Guid QuestionId { get; init; }
        public required long ThroughSequence { get; init; }
        public required string AlgorithmVersion { get; init; }

        public required string ResultJson { get; init; }
        public required DateTime ServedAtUtc { get; init; }
    }
}
