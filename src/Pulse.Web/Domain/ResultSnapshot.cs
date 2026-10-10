namespace Pulse.Web.Domain
{
    // Represents a summary of answers for a specific question, including counts and percentages.
    public sealed record AnswerSummary(
        VoteAnswer Answer,
        int Count,
        decimal? Percentage,
        decimal? AverageConfidence
    );

    // Represents a snapshot of the results for a specific question, including answer summaries and metadata.
    public sealed record ResultSnapshot(
        Guid QuestionId,
        long ThroughSequence,
        string AlgorithmVersion,
        int ParticipantCount,
        int ChangedAnswerCount,
        decimal? ChangedAnswerPercentage,
        AnswerSummary[] Answers
    );
}
