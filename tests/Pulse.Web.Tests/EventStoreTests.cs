using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pulse.Web.Domain;

namespace Pulse.Web.Tests;

public sealed class EventStoreTests : DatabaseTest
{
    [Fact]
    public async Task Retrying_an_old_request_does_not_revert_a_newer_vote_and_changed_content_is_rejected()
    {
        var question = await QuestionAsync();
        var voter = User();
        var request = Guid.NewGuid();
        var original = await Votes.CastAsync(voter, question, request, VoteAnswer.Yes, 50);
        await VoteAsync(voter, question, VoteAnswer.No, 80);
        var retried = await Votes.CastAsync(voter, question, request, VoteAnswer.Yes, 50);
        Assert.True(retried.IsDuplicate);
        Assert.Equal(original.Sequence, retried.Sequence);
        Assert.Equal(new VoteCastPayload(VoteAnswer.No, 80), await Votes.GetLatestAsync(voter, question));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Votes.CastAsync(voter, question, request, VoteAnswer.Unsure, 30));
        Assert.Equal(2, (await EventsAsync()).Count(e => e.Kind == PulseEventKind.VoteCast));
    }

    [Fact]
    public async Task Concurrent_identical_votes_with_different_request_ids_append_only_once()
    {
        var question = await QuestionAsync();
        var voter = User();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            return await VoteAsync(voter, question, VoteAnswer.Yes, 50);
        })).ToArray();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);
        Assert.Single(results, r => !r.IsUnchanged);
        Assert.Single(results.Select(r => r.Sequence).Distinct());
        Assert.Single(await EventsAsync(), e => e.Kind == PulseEventKind.VoteCast);
        Assert.True((await Chain.VerifyAsync(Admin)).IsValid);
    }

    [Fact]
    public async Task Concurrent_writers_across_questions_get_unique_sequences_in_one_valid_chain()
    {
        var questions = new[] { await QuestionAsync(), await QuestionAsync() };
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 6).Select(i => Task.Run(async () =>
        {
            await gate.Task;
            return await VoteAsync(User(), questions[i % 2], VoteAnswer.Yes, 50);
        })).ToArray();
        gate.SetResult();
        var writes = await Task.WhenAll(tasks);
        Assert.Equal(6, writes.Select(w => w.Sequence).Distinct().Count());
        var events = await EventsAsync();
        Assert.Equal(8, events.Length);
        for (var i = 1; i < events.Length; i++)
            Assert.Equal(events[i - 1].Hash, events[i].PreviousHash);
        Assert.True((await Chain.VerifyAsync(Admin)).IsValid);
        foreach (var question in questions)
            Assert.Equal(3, (await Results.GetCurrentAsync(question)).ParticipantCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Ef_guard_rejects_updates_and_deletes_of_events_and_presentations(bool presentation, bool delete)
    {
        var question = await QuestionAsync();
        await Results.GetCurrentAsync(question);
        await using var db = CreateDbContext();
        if (presentation)
        {
            var record = await db.PresentedResults.SingleAsync();
            if (delete) db.Remove(record);
            else db.Entry(record).Property(r => r.ResultJson).CurrentValue = "{}";
        }
        else
        {
            var record = await db.Events.SingleAsync();
            if (delete) db.Remove(record);
            else db.Entry(record).Property(e => e.PayloadJson).CurrentValue = "{}";
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        await using var read = CreateDbContext();
        Assert.Equal(1, await read.Events.CountAsync());
        Assert.Equal(1, await read.PresentedResults.CountAsync());
        Assert.True((await Chain.VerifyAsync(Admin)).IsValid);
        Assert.True(Assert.Single(await Results.CompareStoredAsync(Admin, question)).Matches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Chain_detects_a_changed_payload_or_deleted_middle_event(bool delete)
    {
        var question = await QuestionAsync();
        var voter = User();
        var middle = await VoteAsync(voter, question, VoteAnswer.Yes, 50);
        var next = await VoteAsync(voter, question, VoteAnswer.No, 80);
        Assert.True((await Chain.VerifyAsync(Admin)).IsValid);
        await using var db = CreateDbContext();
        // Simulate out-of-band database tampering without recomputing the hashes.
        if (delete)
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Events WHERE Sequence = {middle.Sequence}");
        else
        {
            var payload = JsonSerializer.Serialize(new VoteCastPayload(VoteAnswer.Unsure, 50));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Events SET PayloadJson = {payload} WHERE Sequence = {middle.Sequence}");
        }
        var verification = await Chain.VerifyAsync(Admin);
        Assert.False(verification.IsValid);
        Assert.Equal(delete ? next.Sequence : middle.Sequence, verification.ProblemSequence);
    }
}
