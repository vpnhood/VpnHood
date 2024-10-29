using System.Text.Json;
using VpnHood.Common.Tokens;
using VpnHood.Common.Utils;

namespace VpnHood.Manager.Common.ClientPolicies;
public static class ClientPolicyUtils
{

    public static ClientPolicy[]? ArrayFromString(string? clientPolicies)
    {
        return clientPolicies == null ? null : VhUtil.JsonDeserialize<ClientPolicy[]>(clientPolicies);
    }

    public static string? ArrayToString(ClientPolicy[]? clientPolicies)
    {
        return clientPolicies == null ? null : JsonSerializer.Serialize(clientPolicies);
    }
}
