namespace VpnHood.Core.Client.Abstractions;

public enum ClientState
{
    None,
    Connecting,
    Connected,
    Waiting,
    Disconnecting,
    Disposed
}