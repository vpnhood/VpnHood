namespace VpnHood.Core.Server;

public enum ServerState
{
    NotStarted,
    Waiting,
    Configuring,
    Ready,
    Disposed
}