using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.Common.Messaging;
using VpnHood.Common.TokenLegacy;

namespace VpnHood.Server.Access.Managers.File;

[Obsolete("deprecated in version 3.3.450 or upper")]
internal class FileAccessManagerLegacyV3
{
    public class AccessItemV3
    {
        public DateTime? ExpirationTime { get; set; }
        public int MaxClientCount { get; set; }
        public long MaxTraffic { get; set; }
        public TokenV3 Token { get; set; } = null!;

        [JsonIgnore]
        public AccessUsage AccessUsage { get; set; } = new();
    }

    public static void MigrateLegacyTokensV3(string storagePath)
    {
        var versionInfoFile = Path.Combine(storagePath, "version");
        var version = System.IO.File.Exists(versionInfoFile)
            ? int.Parse(System.IO.File.ReadAllText(versionInfoFile)) : 3;

        if (version >= 4)
            return;

        Directory.GetFiles(storagePath, "*.token")
            .Select(x => new { Path = x, Json = System.IO.File.ReadAllText(x) })
            .ToList()
            .ForEach(x =>
            {
                try
                {
                    var accessItemV3 = JsonSerializer.Deserialize<AccessItemV3>(x.Json);
                    if (accessItemV3 == null || accessItemV3.Token.Version > 3)
                        return;

                    var accessItem = new FileAccessManager.AccessItem
                    {
                        ExpirationTime = accessItemV3.ExpirationTime,
                        MaxClientCount = accessItemV3.MaxClientCount,
                        MaxTraffic = accessItemV3.MaxTraffic,
                        Token = accessItemV3.Token.ToToken()
                    };

                    var newToken = JsonSerializer.Serialize(accessItem);
                    var backupPath = Path.Combine(storagePath, "backup", "v3");
                    Directory.CreateDirectory(backupPath);
                    System.IO.File.Move(x.Path, Path.Combine(backupPath, Path.GetFileName(x.Path)));
                    System.IO.File.WriteAllText(x.Path, newToken);
                }
                catch
                {
                    // continue
                }
            });

        System.IO.File.WriteAllText(versionInfoFile, "4");
    }
}
