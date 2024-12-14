using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.Server.Access.Managers.FileAccessManagers.Dtos;

namespace VpnHood.Core.Server.Access.Managers.FileAccessManagers.Services;

internal class AccessTokenLegacyConverter
{
    public static void ConvertToken1ToToken2(string storagePath, string token2Extension)
    {
        try {
            if (!Directory.Exists(storagePath))
                return;

            var files = Directory.GetFiles(storagePath, "*.token");
            if (files.Length > 0) {
                VhLogger.Instance.LogInformation("Converting old token1 files to Token2 format. TokenCount: {TokenCount}", files.Length);
            }

            foreach (var file in files) {
                try {
                    var accessTokenLegacy = VhUtil.JsonDeserializeFile<AccessTokenLegacy>(file);
                    if (!string.IsNullOrEmpty(accessTokenLegacy?.Token.TokenId)) {

                        var accessToken = new AccessToken {
                            TokenId = accessTokenLegacy.Token.TokenId,
                            IssuedAt = accessTokenLegacy.Token.IssuedAt,
                            MaxClientCount = accessTokenLegacy.MaxClientCount,
                            MaxTraffic = accessTokenLegacy.MaxTraffic,
                            ExpirationTime = accessTokenLegacy.ExpirationTime,
                            AdRequirement = accessTokenLegacy.AdRequirement,
                            Secret = accessTokenLegacy.Token.Secret,
                            Name = accessTokenLegacy.Token.Name,
                        };

                        File.WriteAllText(Path.ChangeExtension(file, token2Extension),
                            JsonSerializer.Serialize(accessToken));
                    }

                    var backupPath = Path.Combine(storagePath, "backup-tokens-v1");
                    Directory.CreateDirectory(backupPath);
                    File.Move(file, Path.Combine(backupPath, Path.GetFileName(file)));

                }
                catch (Exception ex) {
                    VhLogger.Instance.LogError(ex, "Could not convert old token file. FilePath: {FilePath}", file);
                }
            }
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not convert old token files.");
        }
    }
}
