namespace VpnHood.Core.Client;

public interface IClientSession
{
    ISessionStatus Status { get; }
    ISessionAdHandler AdHandler { get; }
    ClientSessionConfig Config { get; }
}