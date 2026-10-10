namespace Pulse.Web.Domain
{
    public sealed record VoteCastPayload(VoteAnswer Answer, int Confidence);
}
