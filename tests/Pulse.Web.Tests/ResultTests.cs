using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pulse.Web.Domain;

namespace Pulse.Web.Tests;

public sealed class ResultTests : DatabaseTest
{
    [Fact]
    public async Task Latest_votes_count_once_and_past_boundaries_keep_their_original_meaning()
    {
        var question = await QuestionAsync();
        var a = User();
        await VoteAsync(a, question, VoteAnswer.Yes, 20);
        await VoteAsync(User(), question, VoteAnswer.No, 80);
        var original = await VoteAsync(User(), question, VoteAnswer.Unsure, 100);
        var confidenceChange = await VoteAsync(a, question, VoteAnswer.Yes, 60);
        await VoteAsync(a, question, VoteAnswer.No, 60);
        await VoteAsync(a, await QuestionAsync(), VoteAnswer.Yes, 100); // another question must not leak in
        var events = await EventsAsync();

        var past = ResultCalculator.Calculate(question, events, original.Sequence);
        Assert.Equal(3, past.ParticipantCount);
        Assert.Equal(0, past.ChangedAnswerCount);
        Assert.Equal(10m, Answer(past, VoteAnswer.Yes).ConfidenceWeightedPercentage);
        Assert.Equal(50m, Answer(past, VoteAnswer.Unsure).ConfidenceWeightedPercentage);

        var confidenceOnly = ResultCalculator.Calculate(question, events, confidenceChange.Sequence);
        Assert.Equal(0, confidenceOnly.ChangedAnswerCount);
        Assert.Equal(33.3m, Answer(confidenceOnly, VoteAnswer.Yes).Percentage);
        Assert.Equal(25m, Answer(confidenceOnly, VoteAnswer.Yes).ConfidenceWeightedPercentage);

        var current = ResultCalculator.Calculate(question, events, events[^1].Sequence);
        Assert.Equal(3, current.ParticipantCount);
        Assert.Equal(1, current.ChangedAnswerCount);
        Assert.Equal(240L, current.TotalConfidence);
        Assert.Equal(0, Answer(current, VoteAnswer.Yes).Count);
        Assert.Equal(2, Answer(current, VoteAnswer.No).Count);
        Assert.Equal(66.7m, Answer(current, VoteAnswer.No).Percentage);
        Assert.Equal(58.3m, Answer(current, VoteAnswer.No).ConfidenceWeightedPercentage);
        Assert.Equal(5, events.Count(e => e.QuestionId == question && e.Kind == PulseEventKind.VoteCast));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task No_votes_or_zero_confidence_do_not_invent_weighted_support(bool addZeroConfidenceVotes)
    {
        var question = await QuestionAsync();
        if (addZeroConfidenceVotes)
        {
            await VoteAsync(User(), question, VoteAnswer.Yes, 0);
            await VoteAsync(User(), question, VoteAnswer.No, 0);
        }
        var result = await Results.GetCurrentAsync(question);
        Assert.Equal(addZeroConfidenceVotes ? 2 : 0, result.ParticipantCount);
        Assert.Equal(0L, result.TotalConfidence);
        Assert.All(result.Answers, a => Assert.Null(a.ConfidenceWeightedPercentage));
        if (addZeroConfidenceVotes)
            Assert.Equal(50m, Answer(result, VoteAnswer.Yes).Percentage);
        else
            Assert.All(result.Answers, a => { Assert.Null(a.Percentage); Assert.Null(a.AverageConfidence); });
    }

    [Fact]
    public async Task Stored_v1_and_v2_replay_independently_and_altered_weighted_results_do_not_match()
    {
        var question = await QuestionAsync();
        await VoteAsync(User(), question, VoteAnswer.Yes, 20);
        var vote = await VoteAsync(User(), question, VoteAnswer.No, 80);
        // Literal legacy format: no v2 fields. Do not generate the expected record with the calculator under test.
        var legacyJson = $$"""
            {"QuestionId":"{{question}}","ThroughSequence":{{vote.Sequence}},"AlgorithmVersion":"v1",
             "ParticipantCount":2,"ChangedAnswerCount":0,"ChangedAnswerPercentage":0,
             "Answers":[{"Answer":0,"Count":1,"Percentage":50,"AverageConfidence":20},
                        {"Answer":1,"Count":1,"Percentage":50,"AverageConfidence":80},
                        {"Answer":2,"Count":0,"Percentage":0,"AverageConfidence":null}]}
            """;
        await using var db = CreateDbContext();
        db.PresentedResults.Add(new PresentedResult
        {
            QuestionId = question, ThroughSequence = vote.Sequence, AlgorithmVersion = "v1",
            ResultJson = legacyJson, ServedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var current = await Results.GetCurrentAsync(question);
        await Results.GetCurrentAsync(question); // same boundary/version must reuse the presentation
        Assert.Equal("v2", current.AlgorithmVersion);
        Assert.Equal(20m, Answer(current, VoteAnswer.Yes).ConfidenceWeightedPercentage);
        Assert.Equal(2, await db.PresentedResults.CountAsync());
        Assert.All(await Results.CompareStoredAsync(Admin, question), c => Assert.True(c.Matches));
        Assert.Equal(legacyJson, (await db.PresentedResults.AsNoTracking().SingleAsync(r => r.AlgorithmVersion == "v1")).ResultJson);

        var changed = current with
        {
            Answers = current.Answers.Select(a => a.Answer == VoteAnswer.Yes
                ? a with { ConfidenceWeightedPercentage = 99m } : a).ToArray()
        };
        var alteredJson = JsonSerializer.Serialize(changed);
        // Deliberately bypass the application guard, only in this disposable test database.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE PresentedResults SET ResultJson = {alteredJson} WHERE AlgorithmVersion = 'v2'");
        var comparisons = await Results.CompareStoredAsync(Admin, question);
        Assert.True(comparisons.Single(c => c.Stored.AlgorithmVersion == "v1").Matches);
        Assert.False(comparisons.Single(c => c.Stored.AlgorithmVersion == "v2").Matches);
    }

    private static AnswerSummary Answer(ResultSnapshot result, VoteAnswer answer) =>
        result.Answers.Single(a => a.Answer == answer);
}
