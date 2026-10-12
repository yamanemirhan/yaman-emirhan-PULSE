using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Pulse.Web.Domain;

namespace Pulse.Web.Tests;

public sealed class EvidenceTests : DatabaseTest
{
    [Theory]
    [InlineData(PulseRole.Participant, false, EvidenceStatus.Approved)]
    [InlineData(PulseRole.Administrator, false, EvidenceStatus.Rejected)]
    [InlineData(PulseRole.Moderator, true, EvidenceStatus.Approved)]
    [InlineData(PulseRole.Moderator, true, EvidenceStatus.Rejected)]
    public async Task Review_requires_a_moderator_who_is_not_the_submitter(PulseRole role, bool sameIdentity, EvidenceStatus decision)
    {
        var question = await QuestionAsync();
        var authorId = Guid.NewGuid();
        var evidenceId = Guid.NewGuid();
        var submission = await Evidence.SubmitAsync(User(id: authorId), question, evidenceId,
            Guid.NewGuid(), "Pilot findings", "https://example.org/study", "The trial covered two teams.");
        // Keeping the same ID while switching roles must not permit reviewing your own submission.
        var reviewer = User(role, sameIdentity ? authorId : Guid.NewGuid());
        var action = () => Evidence.ReviewAsync(reviewer, question, evidenceId, Guid.NewGuid(),
            submission.Sequence, decision, "Review decision");
        if (sameIdentity)
            await Assert.ThrowsAsync<ValidationException>(action);
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(action);
        Assert.Equal(2, (await EventsAsync()).Length);
    }

    [Fact]
    public async Task Timeline_preserves_evidence_availability_without_exposing_private_content()
    {
        var question = await QuestionAsync();
        var author = User();
        var viewer = User();
        var moderator = User(PulseRole.Moderator);
        var evidenceId = Guid.NewGuid();
        var beforeEvidence = await VoteAsync(viewer, question, VoteAnswer.No, 80);
        var submission = await Evidence.SubmitAsync(author, question, evidenceId, Guid.NewGuid(),
            "Private source title", "https://example.org/private-source", "Private source summary");
        Assert.Empty(await Evidence.ListAsync(viewer, question));
        Assert.Single(await Evidence.ListAsync(author, question));
        var pendingVote = await VoteAsync(viewer, question, VoteAnswer.Unsure, 50);
        var approval = await Evidence.ReviewAsync(moderator, question, evidenceId, Guid.NewGuid(),
            submission.Sequence, EvidenceStatus.Approved, "Accepted for display");
        var approvedVote = await VoteAsync(viewer, question, VoteAnswer.Yes, 60);
        Assert.Single(await Evidence.ListAsync(viewer, question));
        await Evidence.ReviewAsync(moderator, question, evidenceId, Guid.NewGuid(),
            approval.Sequence, EvidenceStatus.Rejected, "Private review reason");
        Assert.Empty(await Evidence.ListAsync(viewer, question));
        Assert.Empty(await Evidence.ListAsync(viewer, question, pendingVote.Sequence));
        Assert.Single(await Evidence.ListAsync(viewer, question, approvedVote.Sequence));

        var entries = await Timeline.GetAsync(viewer, question);
        Assert.Empty(entries.Single(e => e.Sequence == beforeEvidence.Sequence).Sources);
        Assert.Empty(entries.Single(e => e.Sequence == pendingVote.Sequence).Sources);
        var previouslyVisible = Assert.Single(entries.Single(e => e.Sequence == approvedVote.Sequence).Sources);
        Assert.Null(previouslyVisible.Url);
        Assert.Equal("Previously available source (currently hidden)", previouslyVisible.Title);
        Assert.DoesNotContain(entries, e => e.Sequence == submission.Sequence);
        Assert.All(entries, e => Assert.DoesNotContain("Private review reason", e.Description));
        Assert.All(entries.SelectMany(e => e.Sources), s => Assert.Null(s.Url));

        var before = (await EventsAsync()).Length;
        var trend = await Timeline.GetTrendAsync(viewer, question);
        Assert.Equal(2, trend.Markers.Count); // public approval and withdrawal, not private submission
        Assert.All(trend.Markers.SelectMany(m => m.Sources), s => Assert.Null(s.Url));
        Assert.Equal(before, (await EventsAsync()).Length);
        await using var db = CreateDbContext();
        Assert.Empty(await db.PresentedResults.ToListAsync()); // reconstruction is read-only
    }

    [Fact]
    public async Task A_stale_review_cannot_overwrite_another_moderators_decision()
    {
        var question = await QuestionAsync();
        var evidenceId = Guid.NewGuid();
        var submission = await Evidence.SubmitAsync(User(), question, evidenceId, Guid.NewGuid(),
            "Pilot findings", "https://example.org/study", "Two teams participated.");
        await Evidence.ReviewAsync(User(PulseRole.Moderator), question, evidenceId, Guid.NewGuid(),
            submission.Sequence, EvidenceStatus.Approved, "Accepted");
        await Assert.ThrowsAsync<ValidationException>(() => Evidence.ReviewAsync(User(PulseRole.Moderator),
            question, evidenceId, Guid.NewGuid(), submission.Sequence, EvidenceStatus.Rejected, "Stale decision"));
        Assert.Equal(EvidenceStatus.Approved, Assert.Single(await Evidence.ListAsync(Admin, question)).Status);
        Assert.Equal(3, (await EventsAsync()).Length);
    }
}
