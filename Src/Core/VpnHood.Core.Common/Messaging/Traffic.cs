using System.Text.Json.Serialization;

namespace VpnHood.Core.Common.Messaging;

public readonly record struct Traffic
{
    public long Sent { get; init; }
    public long Received { get; init; }

    [JsonIgnore] 
    public long Total => Sent + Received;

    public Traffic()
    {
    }

    public Traffic(long sent, long received)
    {
        Sent = sent;
        Received = received;
    }

    public static Traffic operator +(Traffic traffic1, Traffic traffic2)
    {
        return new Traffic {
            Sent = traffic1.Sent + traffic2.Sent,
            Received = traffic1.Received + traffic2.Received
        };
    }

    public static Traffic operator -(Traffic traffic1, Traffic traffic2)
    {
        return new Traffic {
            Sent = traffic1.Sent - traffic2.Sent,
            Received = traffic1.Received - traffic2.Received
        };
    }
}