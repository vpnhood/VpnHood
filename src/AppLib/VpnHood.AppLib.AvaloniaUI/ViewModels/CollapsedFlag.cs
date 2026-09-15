using Avalonia.Media.Imaging;
using VpnHood.AppLib.AvaloniaUI.Resources;

namespace VpnHood.AppLib.AvaloniaUI.ViewModels;

// One of the flags a closed server shows in a row (ExpansionPanelCollapsed.vue); the automatic
// choice has no country and shows the earth instead.
public sealed record CollapsedFlag(string? CountryCode)
{
    public bool IsAuto => CountryCode == null;
    public Bitmap? Flag => CountryCode == null ? null : AppAssets.Flag(CountryCode);
}
