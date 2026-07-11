using Android.Content;
using Android.OS;
using Java.Util;
using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.Droid.Common;

public class AndroidAppCultureProvider : IAppCultureProvider
{
    public static bool IsSupported => OperatingSystem.IsAndroidVersionAtLeast(34);

    private AndroidAppCultureProvider()
    {
    }

    public static AndroidAppCultureProvider Create()
    {
        return IsSupported ? new AndroidAppCultureProvider() : throw new NotSupportedException();
    }

    public static AndroidAppCultureProvider? CreateIfSupported()
    {
        return IsSupported ? Create() : null;
    }

    private static LocaleManager GetLocalManager()
    {
        return Application.Context.GetSystemService(Context.LocaleService) as LocaleManager
               ?? throw new Exception("Could not acquire LocaleManager.");
    }

    public string[] SystemCultures {
        get {
            var localeManager = GetLocalManager();
            var languageTags = localeManager.SystemLocales.ToLanguageTags();
            return string.IsNullOrEmpty(languageTags) ? ["en"] : languageTags.Split(',');
        }
    }

    public string[] AvailableCultures {
        get {
            var localeManager = GetLocalManager();
            var languageTags = localeManager.OverrideLocaleConfig?.SupportedLocales?.ToLanguageTags() ?? string.Empty;
            return string.IsNullOrEmpty(languageTags) ? [] : languageTags.Split(',');
        }
        set {
            var localeManager = GetLocalManager();
            localeManager.OverrideLocaleConfig = value.Length > 0
                ? new LocaleConfig(LocaleList.ForLanguageTags(string.Join(",", value)))
                : null;
        }
    }

    public string[] SelectedCultures {
        get {
            var localeManager = GetLocalManager();
            var languageTags = localeManager.ApplicationLocales.ToLanguageTags();
            return string.IsNullOrEmpty(languageTags) ? [] : languageTags.Split(',');
        }
        set {
            var localeManager = GetLocalManager();
            localeManager.ApplicationLocales = new LocaleList(value.Select(x => new Locale(x)).ToArray());
        }
    }
}