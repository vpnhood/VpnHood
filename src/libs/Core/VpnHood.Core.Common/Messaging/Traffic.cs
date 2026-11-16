using System.Text.Json.Serialization;

namespace VpnHood.Core.Common.Messaging;

/// <summary>
/// Represents an amount each way: bytes, or a rate where a member says so.
/// </summary>
public readonly record struct Traffic
{
    /// <summary>Gets the amount sent.</summary>
    public long Sent { get; init; }

    /// <summary>Gets the amount received.</summary>
    public long Received { get; init; }

    /// <summary>Gets the sum of both ways.</summary>
    [JsonIgnore]
    public long Total => Sent + Received;

    /// <summary>Initializes a new instance of the <see cref="Traffic"/> structure, with nothing either way.</summary>
    public Traffic()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="Traffic"/> structure.</summary>
    /// <param name="sent">The amount sent.</param>
    /// <param name="received">The amount received.</param>
    public Traffic(long sent, long received)
    {
        Sent = sent;
        Received = received;
    }

    /// <summary>Adds two amounts, each way.</summary>
    /// <param name="traffic1">The first amount.</param>
    /// <param name="traffic2">The second amount.</param>
    /// <returns>The sum.</returns>
    public static Traffic operator +(Traffic traffic1, Traffic traffic2)
    {
        return new Traffic {
            Sent = traffic1.Sent + traffic2.Sent,
            Received = traffic1.Received + traffic2.Received
        };
    }

    /// <summary>Subtracts an amount from another, each way.</summary>
    /// <param name="traffic1">The amount to subtract from.</param>
    /// <param name="traffic2">The amount to subtract.</param>
    /// <returns>The difference, which may be negative.</returns>
    public static Traffic operator -(Traffic traffic1, Traffic traffic2)
    {
        return new Traffic {
            Sent = traffic1.Sent - traffic2.Sent,
            Received = traffic1.Received - traffic2.Received
        };
    }
}