using Android.Content;
using Android.OS;
using Java.Util;

namespace VpnHood.Client.Device.Droid;

public class AndroidDeviceCultureService : IDeviceCultureService
{
    public string[] SystemCultures
    {
        get
        {
            if (Application.Context.GetSystemService(Context.LocaleService) is not LocaleManager localeManager)
                return ["en"];

            var languageTags = localeManager.SystemLocales.ToLanguageTags();
            return string.IsNullOrEmpty(languageTags) ? ["en"] : languageTags.Split(',');
        }
    }


    public bool IsSelectedCulturesSupported => OperatingSystem.IsAndroidVersionAtLeast(33);
    public string[] SelectedCultures
    {
        get
        {
            if (!IsSelectedCulturesSupported)
                throw new NotSupportedException("SelectedCultures is not supported on this device.");

            if (Application.Context.GetSystemService(Context.LocaleService) is not LocaleManager localeManager)
                return Array.Empty<string>();

            var languageTags = localeManager.ApplicationLocales.ToLanguageTags();
            return string.IsNullOrEmpty(languageTags) ? [] : languageTags.Split(',');
        }
        set
        {
            if (!IsSelectedCulturesSupported)
                throw new NotSupportedException("SelectedCultures is not supported on this device.");

            if (Application.Context.GetSystemService(Context.LocaleService) is LocaleManager localeManager)
                localeManager.ApplicationLocales = new LocaleList(value.Select(x => new Locale(x)).ToArray());
        }
    }

    public bool IsAppCulturesSupported => OperatingSystem.IsAndroidVersionAtLeast(34);
    public string[] AvailableCultures
    {
        get
        {
            if (!IsAppCulturesSupported)
                throw new NotSupportedException("AppCultures is not supported on this device.");

            if (Application.Context.GetSystemService(Context.LocaleService) is not LocaleManager localeManager)
                return [];

            var languageTags = localeManager.OverrideLocaleConfig?.SupportedLocales?.ToLanguageTags() ?? string.Empty;
            return string.IsNullOrEmpty(languageTags) ? [] : languageTags.Split(',');
        }
        set
        {
            if (!IsAppCulturesSupported)
                throw new NotSupportedException("AppCultures is not supported on this device.");

            if (Application.Context.GetSystemService(Context.LocaleService) is LocaleManager localeManager)
            {
                localeManager.OverrideLocaleConfig = value.Length > 0
                    ? new LocaleConfig(LocaleList.ForLanguageTags(string.Join(",", value)))
                    : null;
            }
        }
    }
}