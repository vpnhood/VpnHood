using System.Text.Json;
using GrayMint.Common.Utils;

namespace VpnHood.AccessServer.Persistence.Models;

public class FarmTokenRepoSettings
{
    public required Uri FileUrl { get; set; }
    public required string UploadMethod { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public string? Body { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this);
    public static FarmTokenRepoSettings? FromJson(string? json) =>
        string.IsNullOrEmpty(json) ? null : GmUtil.JsonDeserialize<FarmTokenRepoSettings>(json);

}