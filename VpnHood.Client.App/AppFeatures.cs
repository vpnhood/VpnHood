namespace VpnHood.Client.App;

public class AppFeatures
{
    public required Version Version { get; init; }
    public required Guid? TestServerTokenId { get; init; }
    public required bool IsExcludeAppsSupported { get; init; }
    public required  bool IsIncludeAppsSupported { get; init; }
    public required Uri? UpdateInfoUrl { get; init; }
}