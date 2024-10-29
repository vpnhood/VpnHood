using VpnHood.Common.Tokens;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.Common.Utils;
using System.Text.Json;

namespace VpnHood.Manager.Common.Utils;

public static class AccessTokenExtensions
{
    public static Token ToToken(this AccessTokenModel model, ServerToken serverToken)
    {
        return new Token {
            IssuedAt = DateTime.UtcNow,
            ServerToken = serverToken,
            Secret = model.Secret,
            TokenId = model.AccessTokenId.ToString(),
            Name = model.AccessTokenName,
            SupportId = model.SupportCode.ToString(),
            ClientPolicies = model.ClientPoliciesGet(),
            Tags = TagUtils.TagsFromString(model.Tags)
        };
    }

    public static ClientPolicy[]? ClientPoliciesGet(this AccessTokenModel model)
    {
        return model.ClientPolicies == null ? null : VhUtil.JsonDeserialize<ClientPolicy[]>(model.ClientPolicies);
    }

    public static void ClientPoliciesSet(this AccessTokenModel model, ClientPolicy[]? value)
    {
        model.ClientPolicies = value == null ? null : JsonSerializer.Serialize(value);

    }
}
