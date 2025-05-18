using System.Text.Json.Serialization;

namespace VpnHood.Core.Common.Messaging;

public readonly struct Traffic : IEquatable<Traffic>
{
    public long Sent { get; init; }
    public long Received { get; init; }

    [JsonIgnore] public long Total => Sent + Received;

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

    public override bool Equals(object? obj)
    {
        return obj is Traffic other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Sent, Received);
    }


    public static bool operator ==(Traffic traffic1, Traffic traffic2)
    {
        return traffic1.Equals(traffic2);
    }

    public static bool operator !=(Traffic traffic1, Traffic traffic2)
    {
        return !traffic1.Equals(traffic2);
    }

    public bool Equals(Traffic other)
    {
        return Sent == other.Sent && Received == other.Received;
    }

    public override string ToString()
    {
        return $"Sent: {Sent}, Received: {Received}";
    }
}