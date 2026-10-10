using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Pulse.Web.Domain
{
    public static class EventHasher
    {
        // initial hash, there is no previous event for the first event in the system
        public static readonly string GenesisHash = new('0', 64); 

        public static string Compute(PulseEvent pulseEvent, string previousHash)
        {
            if (pulseEvent.HashVersion != 1)
            {
                throw new NotSupportedException(
                    $"Unsupported hash version: {pulseEvent.HashVersion}");
            }

            if (pulseEvent.RecordedAtUtc.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("Event time must be UTC.");
            }

            using var stream = new MemoryStream();

            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();

                writer.WriteString("Format", "PULSE-EVENT");
                writer.WriteNumber("HashVersion", pulseEvent.HashVersion);
                writer.WriteNumber("Sequence", pulseEvent.Sequence);
                writer.WriteString("QuestionId", pulseEvent.QuestionId.ToString("D"));
                writer.WriteString("Kind", pulseEvent.Kind.ToString());
                writer.WriteString("ActorId", pulseEvent.ActorId.ToString("D"));
                writer.WriteString("ActorRole", pulseEvent.ActorRole.ToString());

                writer.WriteString("RecordedAtUtc", pulseEvent.RecordedAtUtc.ToString("O", CultureInfo.InvariantCulture));

                writer.WriteString("RequestId", pulseEvent.RequestId.ToString("D"));
                writer.WriteNumber("PayloadVersion", pulseEvent.PayloadVersion);
                writer.WriteString("PayloadJson", pulseEvent.PayloadJson);
                writer.WriteString("PreviousHash", previousHash);

                writer.WriteEndObject();
            }

            var hashBytes = SHA256.HashData(stream.ToArray());

            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
    }
}
