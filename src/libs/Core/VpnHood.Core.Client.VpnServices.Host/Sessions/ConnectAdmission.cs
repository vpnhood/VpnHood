namespace VpnHood.Core.Client.VpnServices.Host.Sessions;

// What a connect's begin found: one of the three below, or none of them where the host is gone or a newer connect
// replaced the session a reconnect was for
internal sealed class ConnectAdmission
{
    // the connect, begun
    public BegunConnect? Connect { get; init; }

    // a connection is under way, which a connect that is not forced leaves, with its client where it has one
    public bool IsUnderWay { get; init; }
    public VpnHoodClient? ClientUnderWay { get; init; }

    // a stop came before the connect, which gives way to it: the session that stop ends
    public int? StopSessionId { get; init; }
}
