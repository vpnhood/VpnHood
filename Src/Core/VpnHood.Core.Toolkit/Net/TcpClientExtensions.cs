using System.Net;
using System.Net.Sockets;

namespace VpnHood.Core.Toolkit.Net;

public static class TcpClientExtensions
{
    extension(TcpClient tcpClient)
    {
        public IPEndPoint? TryGetLocalEndPoint()
        {
            // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
            try {
                return tcpClient.Client?.LocalEndPoint as IPEndPoint;
            }
            catch {
                return null;
            }
        }

        public IPEndPoint? TryGetRemoteEndPoint()
        {
            // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
            try {
                return tcpClient.Client?.RemoteEndPoint as IPEndPoint;
            }
            catch {
                return null;
            }
        }
    }
}