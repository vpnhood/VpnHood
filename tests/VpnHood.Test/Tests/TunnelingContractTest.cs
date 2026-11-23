using System.Reflection;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.Messaging;

namespace VpnHood.Test.Tests;

// What the tunnel's types promise: event IDs no code can replace, and the numbers the client and the server agree on
[TestClass]
public class TunnelingContractTest
{
    [TestMethod]
    public void The_event_ids_cannot_be_replaced()
    {
        // any code could set one for the whole process
        var fields = typeof(GeneralEventId).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(x => x.FieldType == typeof(EventId))
            .ToArray();

        Assert.IsNotEmpty(fields);
        foreach (var field in fields)
            Assert.IsTrue(field.IsInitOnly, $"{field.Name} should be read-only.");

        Assert.AreEqual(nameof(GeneralEventId.StreamForwardingChannel), GeneralEventId.StreamForwardingChannel.Name);
    }

    [TestMethod]
    public void The_stream_types_keep_their_numbers()
    {
        // the header may name a type by its number, as the server reads it; 2 was a type no version used
        CollectionAssert.AreEquivalent(new Dictionary<string, int> {
            ["Unknown"] = 0, ["None"] = 1, ["WebSocket"] = 3, ["WebSocketNoHandshake"] = 4
        }, Enum.GetValues<TunnelStreamType>().ToDictionary(x => x.ToString(), x => (int)x));
    }

    [TestMethod]
    public void The_request_codes_keep_their_numbers()
    {
        // 5 was a request no client sent, which a server before its removal reads and fails on
        CollectionAssert.AreEquivalent(new Dictionary<string, byte> {
            ["Hello"] = 1, ["TcpPacketChannel"] = 2, ["TcpForwardingChannel"] = 3, ["SessionStatus"] = 4,
            ["RewardedAd"] = 10, ["ServerCheck"] = 30, ["Bye"] = 50
        }, Enum.GetValues<RequestCode>().ToDictionary(x => x.ToString(), x => (byte)x));
    }
}
