using System.Text.Json;

namespace Pulse.Web.Domain;

public sealed record TimelineSource(string Title, string? Url);
public sealed record TimelineEntry(long Sequence, DateTime RecordedAtUtc, string Title,
    string Description, ResultSnapshot? Result, IReadOnlyList<TimelineSource> Sources);

public static class QuestionTimeline
{
    public static IReadOnlyList<TimelineEntry> Build(Guid questionId,
        IEnumerable<PulseEvent> events, Guid viewerId, PulseRole viewerRole)
    {
        var history = events.Where(e => e.QuestionId == questionId).OrderBy(e => e.Sequence).ToArray();
        var currentEvidence = EvidenceHistory.Replay(questionId, history).ToDictionary(e => e.Id);
        var staff = viewerRole is PulseRole.Moderator or PulseRole.Administrator;
        var votes = new Dictionary<Guid, VoteCastPayload>();
        var entries = new List<TimelineEntry>();

        bool CanRead(EvidenceDetails evidence) => staff || evidence.SubmittedBy == viewerId ||
            evidence.Status == EvidenceStatus.Approved;
        TimelineSource Source(EvidenceDetails evidence) => CanRead(currentEvidence[evidence.Id])
            ? new(evidence.Title, evidence.Url)
            : new("Previously available source (currently hidden)", null);

        foreach (var item in history)
        {
            if (item.Kind == PulseEventKind.VoteCast)
            {
                // Use the same versioned calculation as current results; this does not store a presentation.
                var result = ResultCalculator.Calculate(questionId, history, item.Sequence);
                var vote = JsonSerializer.Deserialize<VoteCastPayload>(item.PayloadJson)!;
                votes.TryGetValue(item.ActorId, out var previous);
                var own = item.ActorId == viewerId;
                var title = previous is null ? (own ? "You voted" : "A participant voted")
                    : own ? "You updated your vote" : "A participant updated their vote";
                var description = own
                    ? previous is null ? $"{vote.Answer} · {vote.Confidence}/100 confidence"
                        : $"{previous.Answer} · {previous.Confidence}/100 → {vote.Answer} · {vote.Confidence}/100"
                    : "The group result below counts each participant's latest vote at this point.";
                var sources = EvidenceHistory.Replay(questionId, history, item.Sequence)
                    .Where(e => e.Status == EvidenceStatus.Approved).Select(Source).ToArray();
                entries.Add(new(item.Sequence, item.RecordedAtUtc, title, description, result, sources));
                votes[item.ActorId] = vote;
            }
            else if (item.Kind == PulseEventKind.EvidenceSubmitted)
            {
                var payload = EvidenceHistory.Read<EvidenceSubmittedPayload>(item.PayloadVersion, item.PayloadJson);
                var evidence = currentEvidence[payload.EvidenceId];
                // A pending submission was not public, even if it was approved later.
                if (staff || evidence.SubmittedBy == viewerId)
                    entries.Add(new(item.Sequence, item.RecordedAtUtc, "Evidence submitted",
                        "Pending review; not yet visible to the group.", null, [Source(evidence)]));
            }
            else if (item.Kind == PulseEventKind.EvidenceReviewed)
            {
                var payload = EvidenceHistory.Read<EvidenceReviewedPayload>(item.PayloadVersion, item.PayloadJson);
                var evidence = currentEvidence[payload.EvidenceId];
                var privileged = staff || evidence.SubmittedBy == viewerId;
                var before = EvidenceHistory.Replay(questionId, history, item.Sequence - 1)
                    .Single(e => e.Id == payload.EvidenceId);
                if (!privileged && payload.Status != EvidenceStatus.Approved && before.Status != EvidenceStatus.Approved)
                    continue;
                var title = payload.Status == EvidenceStatus.Approved ? "Evidence approved" : "Evidence rejected";
                var description = payload.Status == EvidenceStatus.Approved
                    ? "Available to the group from this point." : "Not available to the group from this point.";
                if (privileged) description += $" Reason: {payload.Reason}";
                entries.Add(new(item.Sequence, item.RecordedAtUtc, title, description, null, [Source(evidence)]));
            }
        }
        return entries.OrderByDescending(e => e.Sequence).ToArray();
    }
}
