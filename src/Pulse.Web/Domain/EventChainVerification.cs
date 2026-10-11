namespace Pulse.Web.Domain
{
    public sealed record EventChainVerification(
    long CheckedEvents,
    long? LastVerifiedSequence,
    long? ProblemSequence,
    string? Problem)
    {
        public bool IsValid => Problem is null;
    }
}