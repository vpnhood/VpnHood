namespace VpnHood.Client.App;

public class AppFeatures
{
    public required Version Version { get; init; }
    public required string? TestServerTokenId { get; init; }
    public required bool IsExcludeAppsSupported { get; init; }
    public required  bool IsIncludeAppsSupported { get; init; }
    public required Uri? UpdateInfoUrl { get; init; }
    public required string? UiName { get; init; }
    public required bool IsAddServerSupported { get; init; }
}