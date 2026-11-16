using System.Net;
using System.Net.Sockets;

namespace VpnHood.Net.Toolkit.Net;

/// <summary>
/// Provides a TCP client's endpoints as <see cref="IPEndPoint"/>s.
/// </summary>
public static class TcpClientExtensions
{
    /// <summary>Provides a TCP client's endpoints.</summary>
    /// <param name="tcpClient">The TCP client.</param>
    extension(TcpClient tcpClient)
    {
        /// <summary>Gets the client's local endpoint, or <see langword="null"/> when it has none or cannot tell.</summary>
        /// <returns>The endpoint, or <see langword="null"/>.</returns>
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

        /// <summary>Gets the client's remote endpoint, or <see langword="null"/> when it has none or cannot tell.</summary>
        /// <returns>The endpoint, or <see langword="null"/>.</returns>
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

        /// <summary>Gets the client's local endpoint.</summary>
        /// <returns>The endpoint.</returns>
        /// <exception cref="ArgumentNullException">The client has no socket.</exception>
        /// <exception cref="InvalidOperationException">The socket has no local endpoint.</exception>
        /// <exception cref="InvalidCastException">The endpoint is not an IP one.</exception>
        public IPEndPoint GetLocalEndPoint()
        {
            ArgumentNullException.ThrowIfNull(tcpClient.Client, nameof(tcpClient.Client));
            return tcpClient.Client.GetLocalEndPoint();
        }

        /// <summary>Gets the client's remote endpoint.</summary>
        /// <returns>The endpoint.</returns>
        /// <exception cref="ArgumentNullException">The client has no socket.</exception>
        /// <exception cref="InvalidOperationException">The socket has no remote endpoint.</exception>
        /// <exception cref="InvalidCastException">The endpoint is not an IP one.</exception>
        public IPEndPoint GetRemoteEndPoint()
        {
            ArgumentNullException.ThrowIfNull(tcpClient.Client, nameof(tcpClient.Client));
            return tcpClient.Client.GetRemoteEndPoint();
        }
    }
}