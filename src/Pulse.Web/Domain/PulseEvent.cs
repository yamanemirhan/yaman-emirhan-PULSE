using static Pulse.Web.Domain.Enums;

namespace Pulse.Web.Domain
{
    public sealed class PulseEvent
    {
        public long Sequence { get; private set; } // seq of event 

        public required Guid QuestionId { get; init; }
        public required PulseEventKind Kind { get; init; }

        public required Guid ActorId { get; init; }
        public required PulseRole ActorRole { get; init; }

        public required DateTime RecordedAtUtc { get; init; }
        public required Guid RequestId { get; init; }

        public int PayloadVersion { get; init; } = 1;
        public required string PayloadJson { get; init; } // exp. {"Answer": "No", "Confidence": "70"}

        public int HashVersion { get; private set; } = 1;
        public string PreviousHash { get; private set; } = string.Empty;
        public string Hash { get; private set; } = string.Empty;
    }
}
