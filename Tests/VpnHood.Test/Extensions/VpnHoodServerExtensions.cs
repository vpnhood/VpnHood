using VpnHood.Core.Client;
using VpnHood.Core.Server;
using VpnHood.Core.Toolkit.Exceptions;

namespace VpnHood.Test.Extensions;

public static class VpnHoodServerExtensions
{
    extension(VpnHoodServer server)
    {
        public Session GetSession(VpnHoodClient client)
        {
            return server.SessionManager.GetSessionById(client.SessionId)
                   ?? throw new NotExistsException();
        }
    }
}