namespace VpnHood.Core.Client.Abstractions;

public enum ClientState
{
    None,
    Initializing,
    Connecting,
    Connected,
    Waiting,
    Disconnecting,
    Disposed
}