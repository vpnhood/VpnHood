using System.Globalization;
using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.Providers;

internal class AppCultureProvider(VpnHoodApp vpnHoodApp)
    : IAppCultureProvider
{
    public string[] SystemCultures => [CultureInfo.InstalledUICulture.Name];
    public string[] AvailableCultures { get; set; } = [];

    public string[] SelectedCultures {
        get => vpnHoodApp.UserSettings.CultureCode != null ? [vpnHoodApp.UserSettings.CultureCode] : [];
        set => vpnHoodApp.UserSettings.CultureCode = value.FirstOrDefault();
    }
}