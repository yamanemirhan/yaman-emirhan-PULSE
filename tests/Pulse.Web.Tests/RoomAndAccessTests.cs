using System.ComponentModel.DataAnnotations;
using Pulse.Web.Domain;
using Pulse.Web.Services;

namespace Pulse.Web.Tests;

public sealed class RoomAndAccessTests : DatabaseTest
{
    [Theory]
    [InlineData(PulseRole.Participant)]
    [InlineData(PulseRole.Moderator)]
    public async Task Non_administrators_cannot_feature_questions_or_use_integrity_tools(PulseRole role)
    {
        var question = await QuestionAsync();
        var user = User(role);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Room.FeatureAsync(user, question, Guid.NewGuid(), 0));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Chain.VerifyAsync(user));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Results.CompareStoredAsync(user, question));
        Assert.Null(await Room.GetAsync());
    }

    [Fact]
    public async Task Featured_question_is_persisted_and_stale_updates_are_rejected()
    {
        var first = await QuestionAsync();
        var second = await QuestionAsync();
        var feature = await Room.FeatureAsync(Admin, first, Guid.NewGuid(), 0);
        await Assert.ThrowsAsync<ValidationException>(() => Room.FeatureAsync(Admin, second, Guid.NewGuid(), 0));
        var unchanged = await Room.FeatureAsync(Admin, first, Guid.NewGuid(), feature.Sequence);
        Assert.True(unchanged.IsUnchanged);
        var vote = await VoteAsync(User(), first, VoteAnswer.Yes, 50);

        // A new service instance reads persisted state, not an instance's memory.
        var reloaded = await new RoomService(Store, this).GetAsync();
        Assert.NotNull(reloaded);
        Assert.Equal(first, reloaded.QuestionId);
        Assert.Equal(vote.Sequence, reloaded.QuestionSequence);
        await Room.FeatureAsync(Admin, second, Guid.NewGuid(), feature.Sequence);
        Assert.Equal(second, (await Room.GetAsync())!.QuestionId);
        Assert.True((await Chain.VerifyAsync(Admin)).IsValid);
    }
}
