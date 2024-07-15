using System.Text.Json.Serialization;

namespace VpnHood.Common.Messaging;

public class Traffic : IEquatable<Traffic>, ICloneable
{
    public long ReceivedTraffic { set => Received = value; } 

    public long Sent { get; set; }
    public long Received { get; set; }

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

    public void Add(Traffic traffic)
    {
        Sent += traffic.Sent;
        Received += traffic.Received;
    }

    public static Traffic operator +(Traffic traffic1, Traffic traffic2)
    {
        return new Traffic
        {
            Sent = traffic1.Sent + traffic2.Sent,
            Received = traffic1.Received + traffic2.Received
        };
    }

    public static Traffic operator -(Traffic traffic1, Traffic traffic2)
    {
        return new Traffic
        {
            Sent = traffic1.Sent - traffic2.Sent,
            Received = traffic1.Received - traffic2.Received
        };
    }

    public bool Equals(Traffic? other)
    {
        if (other == null) return false;
        return Sent == other.Sent && Received == other.Received;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as Traffic);
    }

    public override int GetHashCode()
    {
        // ReSharper disable NonReadonlyMemberInGetHashCode
        return HashCode.Combine(Sent, Received);
        // ReSharper restore NonReadonlyMemberInGetHashCode
    }

    public Traffic Clone()
    {
        return new Traffic
        {
            Sent = Sent,
            Received = Received
        };
    }

    object ICloneable.Clone()
    {
        return Clone();
    }

    public static bool operator ==(Traffic? traffic1, Traffic? traffic2)
    {
        if (ReferenceEquals(traffic1, traffic2)) return true;
        if (traffic1 is null || traffic2 is null) return false;
        return traffic1.Equals(traffic2);
    }

    public static bool operator !=(Traffic traffic1, Traffic traffic2)
    {
        return !(traffic1 == traffic2);
    }
}