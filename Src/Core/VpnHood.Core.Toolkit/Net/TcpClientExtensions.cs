using System.Net;
using System.Net.Sockets;

namespace VpnHood.Core.Toolkit.Net;

public static class TcpClientExtensions
{
    public static IPEndPoint? TryGetLocalEndPoint(this TcpClient tcpClient)
    {
        // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
        try {
            return tcpClient.Client?.LocalEndPoint as IPEndPoint;
        }
        catch {
            return null;
        }
    }

    public static IPEndPoint? TryGetRemoteEndPoint(this TcpClient tcpClient)
    {
        // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
        try {
            return tcpClient.Client?.RemoteEndPoint as IPEndPoint;
        }
        catch  {
            return null;
        }
    }
}