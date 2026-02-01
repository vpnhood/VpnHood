using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Tunneling.Connections;

public static class ConnectionExtensions
{
    extension(IConnection connection)
    {
        public IPEndPointPair ToEndPointPair()
        {
            return new IPEndPointPair(connection.LocalEndPoint, connection.RemoteEndPoint);
        }

        public void PreventReuse()
        {
            if (connection is ReusableConnection reusableConnection)
                reusableConnection.PreventReuse();
        }
    }
}