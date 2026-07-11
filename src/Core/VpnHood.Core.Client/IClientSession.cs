using VpnHood.Core.Client.Abstractions;

namespace VpnHood.Core.Client;

public interface IClientSession
{
    SessionInfo Info { get; }
    ISessionStatus Status { get; }
    ISessionAdHandler AdHandler { get; }
    ClientSessionConfig Config { get; }
}