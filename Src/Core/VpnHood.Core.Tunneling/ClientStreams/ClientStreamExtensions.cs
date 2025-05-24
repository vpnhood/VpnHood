namespace VpnHood.Core.Tunneling.ClientStreams;

public static class ClientStreamExtensions
{
    public static void DisposeWithoutReuse(this IClientStream clientStream)
    {
        clientStream.PreventReuse();
        clientStream.Dispose();
    }
}