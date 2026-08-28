using System;
using System.Globalization;
using System.Text;

namespace TheConcernedCat.ConcernedCartographer.Reporting;

/// <summary>Builds a Sentry envelope (v7 wire format) around one event
/// payload (#97): an envelope header line, an item header line with the
/// exact UTF-8 payload length, and the payload itself. Pure, so tests
/// can assert on the complete outgoing body.</summary>
internal static class SentryEnvelopeCodec
{
    public static string Build(string eventJson, string eventId, DateTime sentAtUtc)
    {
        int payloadLength = Encoding.UTF8.GetByteCount(eventJson);
        string sentAt = sentAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        return "{\"event_id\":\"" + eventId + "\",\"sent_at\":\"" + sentAt + "\"}\n" +
            "{\"type\":\"event\",\"length\":" + payloadLength.ToString(CultureInfo.InvariantCulture) + "}\n" +
            eventJson;
    }
}
