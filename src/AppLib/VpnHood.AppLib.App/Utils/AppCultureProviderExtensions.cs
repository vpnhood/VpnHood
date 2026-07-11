using System.Globalization;
using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.Utils;

public static class AppCultureProviderExtensions
{
    public static CultureInfo GetBestCultureInfo(this IAppCultureProvider cultureProvider)
    {
        var availableCultures = cultureProvider.AvailableCultures;
        var systemCulture =
            new CultureInfo(cultureProvider.SystemCultures.FirstOrDefault() ?? CultureInfo.InstalledUICulture.Name);
        var culture = systemCulture;
        while (culture.Name != string.Empty) {
            if (availableCultures.Contains(culture.Name, StringComparer.CurrentCultureIgnoreCase))
                return culture;

            culture = culture.Parent;
        }

        return systemCulture;
    }
}