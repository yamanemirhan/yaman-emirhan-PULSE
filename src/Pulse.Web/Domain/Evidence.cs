namespace Pulse.Web.Domain;

public enum EvidenceStatus { Pending, Approved, Rejected }

public sealed record EvidenceSubmittedPayload(Guid EvidenceId, string Title, string Url, string Summary);
public sealed record EvidenceReviewedPayload(Guid EvidenceId, EvidenceStatus Status, string Reason, long ExpectedSequence);

public sealed record EvidenceDetails(
    Guid Id, string Title, string Url, string Summary,
    Guid SubmittedBy, DateTime SubmittedAtUtc, long SubmittedSequence,
    EvidenceStatus Status, string? ReviewReason, DateTime? ReviewedAtUtc,
    long LastChangeSequence);
