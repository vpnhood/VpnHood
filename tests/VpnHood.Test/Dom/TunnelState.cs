using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server;

namespace VpnHood.Test.Dom;

// the tunnel's state at both ends, added to a transfer check's failure: a check that fails once in a full run then
// says where the traffic stopped. A reply the server sent and the client never received was lost on the way back
internal static class TunnelState
{
    public static async Task Assert(Func<Task> check, VpnHoodClient client, VpnHoodServer server,
        Func<string>? more = null)
    {
        var clientBefore = TryGetSession(client)?.Status.SessionTraffic ?? new Traffic();
        var serverBefore = FindServerSession(client, server)?.TrafficMeter.Traffic ?? new Traffic();
        try {
            await check();
        }
        catch (Exception ex) {
            throw new AssertFailedException(
                $"{ex.GetType().Name}: {ex.Message} {Describe(client, server, clientBefore, serverBefore)} " +
                $"{more?.Invoke()}", ex);
        }
    }

    private static string Describe(VpnHoodClient client, VpnHoodServer server, Traffic clientBefore,
        Traffic serverBefore)
    {
        var session = TryGetSession(client);
        var clientState = session == null
            ? $"Client: {client.State}, no session."
            : $"Client: {client.State}, {session.Status.ChannelProtocol}, " +
              $"PacketChannels: {session.Status.ActivePacketChannelCount}, " +
              $"AdapterStarted: {session.Status.IsAdapterStarted}, " +
              $"Traffic in the check: {Format(session.Status.SessionTraffic - clientBefore)}.";

        var serverSession = FindServerSession(client, server);
        var serverState = serverSession == null
            ? "Server: no session."
            : $"Server: Disposed: {serverSession.IsDisposed}, PacketChannels: {serverSession.Tunnel.PacketChannelCount}, " +
              $"Traffic in the check: {Format(serverSession.TrafficMeter.Traffic - serverBefore)}, " +
              $"LastActivity: {serverSession.LastActivityTime:HH:mm:ss.fff}.";

        return $"{clientState} {serverState}";
    }

    private static string Format(Traffic traffic)
    {
        return $"sent {traffic.Sent} B, received {traffic.Received} B";
    }

    private static Session? FindServerSession(VpnHoodClient client, VpnHoodServer server)
    {
        var session = TryGetSession(client);
        return session == null ? null : server.SessionManager.GetSessionById(ulong.Parse(session.Info.SessionId));
    }

    private static IClientSession? TryGetSession(VpnHoodClient client)
    {
        try {
            return client.RequiredSession;
        }
        catch (InvalidOperationException) {
            return null;
        }
    }
}
