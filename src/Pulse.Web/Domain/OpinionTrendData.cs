namespace Pulse.Web.Domain;

public sealed record OpinionTrendPoint(long Sequence, DateTime RecordedAtUtc, string Activity, ResultSnapshot? Result);
public sealed record OpinionEvidenceMarker(long Sequence, DateTime RecordedAtUtc, string Label,
    string Action, IReadOnlyList<TimelineSource> Sources);
public sealed record OpinionTrendData(string AlgorithmVersion, IReadOnlyList<OpinionTrendPoint> Points,
    IReadOnlyList<OpinionEvidenceMarker> Markers);

public static class OpinionTrendBuilder
{
    // Input has already been filtered for the viewer by QuestionTimeline.
    public static OpinionTrendData Build(IReadOnlyList<TimelineEntry> timeline)
    {
        var points = new List<OpinionTrendPoint>();
        var markers = new List<OpinionEvidenceMarker>();
        ResultSnapshot? latest = null;
        foreach (var entry in timeline.OrderBy(e => e.Sequence))
        {
            if (entry.Result is { } result) latest = result;
            else markers.Add(new(entry.Sequence, entry.RecordedAtUtc, $"E{markers.Count + 1}",
                entry.Title, entry.Sources));
            // Evidence extends the existing step, without inventing a vote or interpolating a trend.
            points.Add(new(entry.Sequence, entry.RecordedAtUtc, entry.Title, latest));
        }
        return new(ResultCalculator.CurrentVersion, points, markers);
    }
}
