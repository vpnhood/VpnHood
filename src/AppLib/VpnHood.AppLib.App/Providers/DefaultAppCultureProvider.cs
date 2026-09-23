using System.Globalization;
using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.App.Providers;

internal class DefaultAppCultureProvider(VpnHoodApp vpnHoodApp)
    : IAppCultureProvider
{
    public string[] SystemCultures => [CultureInfo.InstalledUICulture.Name];
    public IReadOnlyList<string> AvailableCultures { get; set; } = [];

    public string[] SelectedCultures {
        get => vpnHoodApp.UserSettings.CultureCode != null ? [vpnHoodApp.UserSettings.CultureCode] : [];
        set => vpnHoodApp.UserSettings.CultureCode = value.FirstOrDefault();
    }
}