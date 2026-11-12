using System.Globalization;
using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.App.Ios;

// iOS counterpart of AndroidAppCultureProvider, and like it the person's choice lives in the OS: the
// app's own AppleLanguages list, which iOS reads before the device's and which the app's page in iOS
// Settings writes too. That page has a Language row only for an app that declares its languages (the
// build does, from the UI's store: VhWriteLanguages) on a device with more than one. An Apple TV's
// Settings has no page for an app.
// - SystemCultures: the device's languages. NSLocale answers with the app's list when there is one,
//   and no API an app may use reads past it (the device's own setting is the system's, which the
//   UserDefaults reason in the privacy manifest does not cover), so the list carries them behind the
//   language chosen: "zh-Hans-US", "de-US", "en-US", as iOS Settings writes it.
// - AvailableCultures: iOS has no runtime "supported locales" override API (the Android 13+
//   feature), so it is kept in-memory; the head pushes the list in via the app config, same as
//   DefaultAppCultureProvider.
// - SelectedCultures: the first of the app's list, as it was written: iOS Settings adds the region.
internal class IosAppCultureProvider : IAppCultureProvider
{
    private const string AppleLanguagesKey = "AppleLanguages";

    // always available on iOS and on an Apple TV
    public static bool IsSupported => OperatingSystem.IsIOS() || OperatingSystem.IsTvOS();

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

    // The app's own domain alone: reading the key through the defaults would answer with the device's
    // list where the app has none, and "none" is what the device's language being followed looks like.
    private static string[] AppLanguages {
        get {
            var bundleId = NSBundle.MainBundle.BundleIdentifier ??
                           throw new InvalidOperationException("The app's bundle has no identifier.");
            var domain = NSUserDefaults.StandardUserDefaults.PersistentDomainForName(bundleId);
            return domain?[AppleLanguagesKey] is NSArray languages
                ? NSArray.FromArray<NSString>(languages)?.Select(x => x.ToString()).ToArray() ?? []
                : [];
        }
    }

    public string[] SystemCultures {
        get {
            var appLanguages = AppLanguages;
            var languages = appLanguages.Length > 0 ? appLanguages[1..] : NSLocale.PreferredLanguages;
            return languages is { Length: > 0 }
                ? languages
                : [CultureInfo.InstalledUICulture.Name];
        }
    }

    // no OS-level override on iOS; the value is supplied by the app and kept in memory
    public IReadOnlyList<string> AvailableCultures { get; set; } = [];

    public string[] SelectedCultures {
        get {
            var appLanguages = AppLanguages;
            return appLanguages.Length > 0 ? [appLanguages[0]] : [];
        }
        set {
            // back to the device's order, which NSLocale shows only with the app's list gone
            var defaults = NSUserDefaults.StandardUserDefaults;
            defaults.RemoveObject(AppleLanguagesKey);

            var code = value.FirstOrDefault();
            if (!string.IsNullOrEmpty(code))
                defaults.SetValueForKey(NSArray.FromStrings([code, .. NSLocale.PreferredLanguages]), new NSString(AppleLanguagesKey));

            defaults.Synchronize();
        }
    }
}
