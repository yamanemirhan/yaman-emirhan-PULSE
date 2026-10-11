namespace Pulse.Web.Domain;

// Zero means no question had been featured when the Administrator loaded the control.
public sealed record QuestionFeaturedPayload(long ExpectedFeatureSequence);
