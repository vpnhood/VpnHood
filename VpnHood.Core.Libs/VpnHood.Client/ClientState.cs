namespace VpnHood.Client;

public enum ClientState
{
    None,
    Connecting,
    Connected,
    Waiting,
    Disconnecting,
    Disposed
}