using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Tunneling.Connections;

public static class ConnectionExtensions
{
    extension(IStreamConnection streamConnection)
    {
        public IPEndPointPair ToEndPointPair()
        {
            return new IPEndPointPair(streamConnection.LocalEndPoint, streamConnection.RemoteEndPoint);
        }

        public void PreventReuse()
        {
            if (streamConnection is ReusableStreamConnection reusableConnection)
                reusableConnection.PreventReuse();
        }
    }
}