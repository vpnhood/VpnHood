using VpnHood.AppUi.Avalonia.Classic.Resources;

namespace VpnHood.AppUi.Avalonia.Classic.ViewModels;

// One of the flags a closed server shows in a row; the automatic
// choice has no country and shows the earth instead.
public sealed record CollapsedFlag(string? CountryCode)
{
    public bool IsAuto => CountryCode == null;
    public string? FlagPath => AppAssets.FlagPath(CountryCode);
}
