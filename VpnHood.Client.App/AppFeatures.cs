namespace VpnHood.Client.App;

public class AppFeatures
{
    public required Version Version { get; init; }
    public required bool IsExcludeAppsSupported { get; init; }
    public required  bool IsIncludeAppsSupported { get; init; }
    public required Uri? UpdateInfoUrl { get; init; }
    public required string? UiName { get; init; }
    public required bool IsAddAccessKeySupported { get; init; }
    public required Guid? BuiltInClientProfileId { get; init; }
}