using Avalonia;
using VpnHood.AppUi.Services;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Resources;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.ViewModels;

// One row of the location list, the web UI's LocationListItem: what the app needs to connect
// there, and what the person sees. A record with value equality, so the view model replaces the
// list only when a row differs; the flag is looked up on demand, never compared.
public sealed record LocationItem(
    Guid ClientProfileId,
    string ServerLocation,
    string CountryCode,
    string Name,
    bool IsNested,
    bool IsAuto,
    bool IsActive,
    bool IsPremiumGroup,
    bool HasUnblockable,
    bool ShowCrown)
{
    public string? FlagPath => IsAuto ? null : AppAssets.FlagPath(CountryCode);
    public Thickness Indent => IsNested ? new Thickness(16, 0, 0, 0) : default;
    public string RecommendedText => $"({Strings.Current.Recommended})";
    public string ActiveChipText => Strings.Current.Active.ToUpperInvariant();
}
