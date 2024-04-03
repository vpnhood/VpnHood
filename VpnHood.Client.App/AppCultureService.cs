using System.Globalization;
using VpnHood.Client.Device;

namespace VpnHood.Client.App;

internal class AppCultureService(VpnHoodApp vpnHoodApp) : ICultureService
{
    public string[] SystemCultures => [CultureInfo.InstalledUICulture.Name];
    public string[] AvailableCultures { get; set; } = [];
    public string[] SelectedCultures
    {
        get => vpnHoodApp.UserSettings.CultureCode != null ? [vpnHoodApp.UserSettings.CultureCode] : [];
        set => vpnHoodApp.UserSettings.CultureCode = value.FirstOrDefault();
    }
}