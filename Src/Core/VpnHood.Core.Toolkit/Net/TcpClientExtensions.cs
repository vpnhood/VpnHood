using System.Net;
using System.Net.Sockets;

namespace VpnHood.Core.Toolkit.Net;

public static class TcpClientExtensions
{
    public static IPEndPoint? SafeLocalEndPoint(this TcpClient tcpClient)
    {
        // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
        return tcpClient.Client?.LocalEndPoint as IPEndPoint;
    }

    public static IPEndPoint? SafeRemoteEndPoint(this TcpClient tcpClient)
    {
        // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
        return tcpClient.Client?.RemoteEndPoint as IPEndPoint;
    }
}