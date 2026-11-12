using VpnHood.Core.Client;
using VpnHood.Core.Server;
using VpnHood.Net.Toolkit.Exceptions;

namespace VpnHood.Test.Extensions;

internal static class VpnHoodServerExtensions
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