using Android.Content;
using Android.OS;
using Java.Util;

namespace VpnHood.Client.Device.Droid;

public class AndroidCultureService : ICultureService
{
    public static bool IsSupported => OperatingSystem.IsAndroidVersionAtLeast(34);

    private AndroidCultureService()
    {
    }

    public static AndroidCultureService Create()
    {
        return IsSupported ? new AndroidCultureService() : throw new NotSupportedException();
    }

    public static AndroidCultureService? CreateIfSupported()
    {
        return IsSupported ? Create() : null;
    }

    private static LocaleManager GetLocalManager()
    {
        return Application.Context.GetSystemService(Context.LocaleService) as LocaleManager
               ?? throw new Exception("Could not acquire LocaleManager.");
    }

    public string[] SystemCultures
    {
        get
        {
            var localeManager = GetLocalManager();
            var languageTags = localeManager.SystemLocales.ToLanguageTags();
            return string.IsNullOrEmpty(languageTags) ? ["en"] : languageTags.Split(',');
        }
    }

    public string[] AvailableCultures
    {
        get
        {
            var localeManager = GetLocalManager();
            var languageTags = localeManager.OverrideLocaleConfig?.SupportedLocales?.ToLanguageTags() ?? string.Empty;
            return string.IsNullOrEmpty(languageTags) ? [] : languageTags.Split(',');
        }
        set
        {
            var localeManager = GetLocalManager();
            localeManager.OverrideLocaleConfig = value.Length > 0
                ? new LocaleConfig(LocaleList.ForLanguageTags(string.Join(",", value)))
                : null;
        }
    }

    public string[] SelectedCultures
    {
        get
        {
            var localeManager = GetLocalManager();
            var languageTags = localeManager.ApplicationLocales.ToLanguageTags();
            return string.IsNullOrEmpty(languageTags) ? [] : languageTags.Split(',');
        }
        set
        {
            var localeManager = GetLocalManager();
            localeManager.ApplicationLocales = new LocaleList(value.Select(x => new Locale(x)).ToArray());
        }
    }
}