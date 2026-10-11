using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Pulse.Web.Domain;

public static class EvidenceHistory
{
    public static IReadOnlyList<EvidenceDetails> Replay(
        Guid questionId, IEnumerable<PulseEvent> events, long throughSequence = long.MaxValue)
    {
        var items = new Dictionary<Guid, EvidenceDetails>();
        foreach (var item in events.Where(e => e.QuestionId == questionId && e.Sequence <= throughSequence)
            .OrderBy(e => e.Sequence))
        {
            if (item.Kind == PulseEventKind.EvidenceSubmitted)
            {
                var payload = Read<EvidenceSubmittedPayload>(item.PayloadVersion, item.PayloadJson);
                Validate(payload);
                if (!items.TryAdd(payload.EvidenceId, new EvidenceDetails(
                    payload.EvidenceId, payload.Title, payload.Url, payload.Summary,
                    item.ActorId, item.RecordedAtUtc, item.Sequence,
                    EvidenceStatus.Pending, null, null, item.Sequence)))
                    throw new InvalidOperationException("Duplicate evidence ID in history.");
            }
            else if (item.Kind == PulseEventKind.EvidenceReviewed)
            {
                var payload = Read<EvidenceReviewedPayload>(item.PayloadVersion, item.PayloadJson);
                Validate(payload);
                if (item.ActorRole != PulseRole.Moderator ||
                    !items.TryGetValue(payload.EvidenceId, out var current) ||
                    current.LastChangeSequence != payload.ExpectedSequence)
                    throw new InvalidOperationException("Invalid evidence review history.");

                items[payload.EvidenceId] = current with
                {
                    Status = payload.Status,
                    ReviewReason = payload.Reason,
                    ReviewedAtUtc = item.RecordedAtUtc,
                    LastChangeSequence = item.Sequence
                };
            }
        }
        return items.Values.OrderByDescending(e => e.SubmittedSequence).ToArray();
    }

    public static T Read<T>(int version, string json)
    {
        if (version != 1)
            throw new NotSupportedException("Unsupported evidence payload version.");
        return JsonSerializer.Deserialize<T>(json)
            ?? throw new InvalidOperationException("Invalid evidence payload.");
    }

    public static void Validate(EvidenceSubmittedPayload payload)
    {
        if (payload.EvidenceId == Guid.Empty)
            throw new ValidationException("Invalid evidence ID.");
        if (string.IsNullOrWhiteSpace(payload.Title) || payload.Title.Length > 200)
            throw new ValidationException("Enter a title between 1 and 200 characters.");
        if (string.IsNullOrWhiteSpace(payload.Summary) || payload.Summary.Length > 2000)
            throw new ValidationException("Enter a summary between 1 and 2000 characters.");
        if (string.IsNullOrWhiteSpace(payload.Url) || payload.Url.Length > 2048 ||
            !Uri.TryCreate(payload.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ValidationException("Enter an absolute HTTP or HTTPS link without embedded credentials.");
    }

    public static void Validate(EvidenceReviewedPayload payload)
    {
        if (payload.EvidenceId == Guid.Empty || payload.ExpectedSequence <= 0)
            throw new ValidationException("Invalid evidence review.");
        if (payload.Status is not (EvidenceStatus.Approved or EvidenceStatus.Rejected))
            throw new ValidationException("Choose Approved or Rejected.");
        if (string.IsNullOrWhiteSpace(payload.Reason) || payload.Reason.Length > 500)
            throw new ValidationException("Enter a review reason between 1 and 500 characters.");
    }
}
