using Pulse.Web.Domain;

namespace Pulse.Web.Services
{
    public sealed record NewEvent(
        Guid QuestionId,
        PulseEventKind Kind,
        Guid ActorId,
        PulseRole ActorRole,
        Guid RequestId,
        string PayloadJson,
        int PayloadVersion = 1
    );
}
