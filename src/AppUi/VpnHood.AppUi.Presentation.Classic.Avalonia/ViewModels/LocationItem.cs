using Avalonia;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Resources;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.ViewModels;

// One row of the location list, the web UI's LocationListItem: what the app needs to connect
// there, and what the person sees. A record with value equality, so the view model replaces the
// list only when a row differs; the flag and the name are looked up on demand, never compared - the
// lists are built on every refresh, and a country's name is not needed until its row is drawn
// (CountryNames).
public sealed record LocationItem(
    Guid VpnProfileId,
    string ServerLocation,
    string CountryCode,
    string CountryName,
    string RegionName,
    bool IsNested,
    bool IsAuto,
    bool IsActive,
    bool IsPremiumGroup,
    bool HasUnblockable,
    bool ShowCrown)
{
    public string Name => IsAuto ? Strings.Current.Fastest : IsNested ? RegionName : CountryNames.Get(CountryCode, CountryName);
    public string? FlagPath => IsAuto ? null : AppAssets.FlagPath(CountryCode);
    public Thickness Indent => IsNested ? new Thickness(16, 0, 0, 0) : default;
    public string RecommendedText => $"({Strings.Current.Recommended})";
    public string ActiveChipText => Strings.Current.Active.ToUpperInvariant();
}
