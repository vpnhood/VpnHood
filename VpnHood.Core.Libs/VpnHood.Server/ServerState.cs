namespace VpnHood.Server;

public enum ServerState
{
    NotStarted,
    Waiting,
    Configuring,
    Ready,
    Disposed
}