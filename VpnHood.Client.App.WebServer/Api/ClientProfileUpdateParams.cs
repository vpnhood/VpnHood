using VpnHood.Common.Utils;

namespace VpnHood.Client.App.WebServer.Api;

public class ClientProfileUpdateParams
{
    public Patch<string?>? Name { get; set; }
}