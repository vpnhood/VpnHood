using System.Globalization;

namespace VpnHood.AppLib.Abstractions.Extensions;

public static class IAppCultureProviderExtensions
{
    public static CultureInfo GetBestCultureInfo(this IAppCultureProvider cultureProvider)
    {
        var availableCultures = cultureProvider.AvailableCultures;
        var systemCulture = new CultureInfo(cultureProvider.SystemCultures.FirstOrDefault() ?? CultureInfo.InstalledUICulture.Name);
        var culture = systemCulture;
        while (culture.Name != string.Empty) {
            if (availableCultures.Contains(culture.Name, StringComparer.CurrentCultureIgnoreCase))
                return culture;

            culture = culture.Parent;
        }

        return systemCulture;
    }
}