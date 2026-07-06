using System.Globalization;
using Foundation;
using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.Ios.Common;

// iOS counterpart of AndroidAppCultureProvider.
// - SystemCultures: the user's ordered device languages (NSLocale.PreferredLanguages).
// - AvailableCultures: iOS has no runtime "supported locales" override API (the Android 13+
//   feature), so it is kept in-memory; the SPA pushes the list in via the app config, same as
//   DefaultAppCultureProvider.
// - SelectedCultures: persisted in NSUserDefaults. We also mirror the choice into the standard
//   iOS "AppleLanguages" key so the per-app language shows up in the iOS Settings app.
public class IosAppCultureProvider : IAppCultureProvider
{
    // dedicated key so the empty<->empty round-trip is unambiguous (AppleLanguages is never empty)
    private const string SelectedCultureKey = "VpnHoodSelectedCulture";
    private const string AppleLanguagesKey = "AppleLanguages";

    // always available on iOS
    public static bool IsSupported => OperatingSystem.IsIOS();

    private IosAppCultureProvider()
    {
    }

    public static IosAppCultureProvider Create()
    {
        return IsSupported ? new IosAppCultureProvider() : throw new NotSupportedException();
    }

    public static IosAppCultureProvider? CreateIfSupported()
    {
        return IsSupported ? new IosAppCultureProvider() : null;
    }

    public string[] SystemCultures {
        get {
            var languages = NSLocale.PreferredLanguages;
            return languages is { Length: > 0 }
                ? languages
                : [CultureInfo.InstalledUICulture.Name];
        }
    }

    // no OS-level override on iOS; the value is supplied by the app/SPA and kept in memory
    public string[] AvailableCultures { get; set; } = [];

    public string[] SelectedCultures {
        get {
            var code = NSUserDefaults.StandardUserDefaults.StringForKey(SelectedCultureKey);
            return string.IsNullOrEmpty(code) ? [] : [code];
        }
        set {
            var defaults = NSUserDefaults.StandardUserDefaults;
            var code = value.FirstOrDefault();
            if (string.IsNullOrEmpty(code)) {
                defaults.RemoveObject(SelectedCultureKey);
                defaults.RemoveObject(AppleLanguagesKey); // fall back to system order
            }
            else {
                defaults.SetString(code, SelectedCultureKey);
                // surface the choice at the OS level (iOS Settings > App > Language)
                defaults.SetValueForKey(NSArray.FromStrings(code), new NSString(AppleLanguagesKey));
            }

            defaults.Synchronize();
        }
    }
}
