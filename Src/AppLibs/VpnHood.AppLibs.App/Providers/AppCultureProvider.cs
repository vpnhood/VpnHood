using System.Globalization;
using VpnHood.AppLibs.Abstractions;

namespace VpnHood.AppLibs.Providers;

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