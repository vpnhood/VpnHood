using System.Text.Json;
using GrayMint.Common.Utils;
using VpnHood.AccessServer.Persistence.Enums;

namespace VpnHood.AccessServer.Persistence.Models;

public class FarmTokenRepoSettings
{
    public required Uri FileUrl { get; set; }
    public required UploadMethod UploadMethod { get; set; }
    public string? AccessToken { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public Dictionary<string, string> FormData { get; set; } = new();
    public string? Body { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this);
    public static FarmTokenRepoSettings? FromJson(string? json) =>
        string.IsNullOrEmpty(json) ? null : GmUtil.JsonDeserialize<FarmTokenRepoSettings>(json);
}