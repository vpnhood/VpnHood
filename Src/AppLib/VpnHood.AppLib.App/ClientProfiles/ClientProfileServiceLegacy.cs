using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Tokens.TokenLegacy;
using VpnHood.Core.Common.Utils;

namespace VpnHood.AppLib.ClientProfiles;

// deprecated by version 3.2.440 or upper
internal static class ClientProfileServiceLegacy
{
    // ReSharper disable once ClassNeverInstantiated.Local
    // ReSharper disable UnusedAutoPropertyAccessor.Local
    private class ClientProfileLegacy
    {
        public string? Name { get; init; }
        public Guid ClientProfileId { get; init; }
        public required string TokenId { get; init; }
    }
    // ReSharper restore UnusedAutoPropertyAccessor.Local

    public static void Migrate(string folderPath, string clientProfilesFilePath)
    {
        var legacyProfilesFilePath = Path.Combine(folderPath, "profiles.json");
        var legacyTokensFilePath = Path.Combine(folderPath, "tokens.json");
        if (!File.Exists(legacyProfilesFilePath) && !File.Exists(legacyTokensFilePath))
            return;

        try {
            var legacyClientProfiles =
                VhUtil.JsonDeserialize<ClientProfileLegacy[]>(File.ReadAllText(legacyProfilesFilePath));
#pragma warning disable CS0618 // Type or member is obsolete
            var tokens = VhUtil.JsonDeserialize<TokenV3[]>(File.ReadAllText(legacyTokensFilePath))
                .Select(x => x.ToToken()).ToArray();
#pragma warning restore CS0618 // Type or member is obsolete
            var clientProfiles = new List<ClientProfile>();

            // Create a file for each profile
            foreach (var legacyClientProfile in legacyClientProfiles)
                try {
                    var token = tokens.First(x => x.TokenId == legacyClientProfile.TokenId);
                    var clientProfile = new ClientProfile {
                        Token = token,
                        ClientProfileId = legacyClientProfile.ClientProfileId,
                        ClientProfileName = legacyClientProfile.Name
                    };
                    clientProfiles.Add(clientProfile);
                }
                catch (Exception ex) {
                    VhLogger.Instance.LogError(ex, "Could not load token. TokenId: {TokenId}",
                        legacyClientProfile.TokenId);
                }

            var json = JsonSerializer.Serialize(clientProfiles);
            File.WriteAllText(clientProfilesFilePath, json);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not migrate legacy ClientProfiles.");
        }

        try {
            File.Move(legacyProfilesFilePath, legacyProfilesFilePath + ".backup");
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not delete legacy file. FilePath: {FilePath}",
                legacyProfilesFilePath);
        }

        try {
            File.Move(legacyTokensFilePath, legacyTokensFilePath + ".backup");
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not delete a legacy file. FilePath: {FilePath}",
                legacyTokensFilePath);
        }
    }
}