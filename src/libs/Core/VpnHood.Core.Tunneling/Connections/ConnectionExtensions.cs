using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Tunneling.Connections;

/// <summary>Provides extension members of <see cref="IStreamConnection"/>.</summary>
public static class ConnectionExtensions
{
    /// <summary>Provides a connection's endpoints as a pair.</summary>
    /// <param name="streamConnection">The connection.</param>
    extension(IStreamConnection streamConnection)
    {
        /// <summary>Gets the connection's local and remote endpoints as a pair.</summary>
        /// <returns>The pair.</returns>
        public IPEndPointPair ToEndPointPair()
        {
            return new IPEndPointPair(streamConnection.LocalEndPoint, streamConnection.RemoteEndPoint);
        }
    }
}