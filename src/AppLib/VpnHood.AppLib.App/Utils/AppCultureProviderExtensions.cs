using System.Globalization;
using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.App.Utils;

public static class AppCultureProviderExtensions
{
    public static CultureInfo GetBestCultureInfo(this IAppCultureProvider cultureProvider)
    {
        return cultureProvider.ToAvailableCulture(
            new CultureInfo(cultureProvider.SystemCultures.FirstOrDefault() ?? CultureInfo.InstalledUICulture.Name));
    }

    // The nearest culture the app has words for, or the culture itself where it has none: an OS names a
    // language with its region, "zh-Hans-US" for the app's "zh".
    public static CultureInfo ToAvailableCulture(this IAppCultureProvider cultureProvider, CultureInfo culture)
    {
        var availableCultures = cultureProvider.AvailableCultures;
        for (var candidate = culture; candidate.Name != string.Empty; candidate = candidate.Parent) {
            if (availableCultures.Contains(candidate.Name, StringComparer.OrdinalIgnoreCase))
                return candidate;
        }

        return culture;
    }
}
