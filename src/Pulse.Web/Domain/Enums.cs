namespace Pulse.Web.Domain
{
    public class Enums
    {
        public enum VoteAnswer
        {
            Yes,
            No,
            Unsure
        }

        public enum PulseRole
        {
            Participant,
            Moderator,
            Administrator
        }

        public enum PulseEventKind
        {
            QuestionCreated,
            VoteCast,
            EvidenceSubmitted,
            EvidenceReviewed,
            QuestionFeatured
        }
    }
}
