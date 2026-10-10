namespace Pulse.Web.Domain
{
    public sealed record ResultComparison(
      Guid Id,
      DateTime ServedAtUtc,
      ResultSnapshot Stored,
      ResultSnapshot Replayed,
      bool Matches
    );
}
