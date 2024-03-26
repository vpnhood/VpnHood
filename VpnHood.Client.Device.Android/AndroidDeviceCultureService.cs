using Android.Content;
using Android.OS;
using Java.Util;

namespace VpnHood.Client.Device.Droid
{
    public class AndroidDeviceCultureService : IDeviceCultureService
    {
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
                return languageTags.Split(',');

            }
            set
            {
                if (!IsSelectedCulturesSupported)
                    throw new NotSupportedException("SelectedCultures is not supported on this device.");

                if (Application.Context.GetSystemService(Context.LocaleService) is LocaleManager localeManager)
                    localeManager.ApplicationLocales = new LocaleList(value.Select(x => new Locale(x)).ToArray());
            }
        }

        public bool IsAvailableCultureSupported => OperatingSystem.IsAndroidVersionAtLeast(34);
        public string[] AvailableCultures
        {
            get
            {
                if (!IsSelectedCulturesSupported)
                    throw new NotSupportedException("AvailableCultures is not supported on this device.");

                if (Application.Context.GetSystemService(Context.LocaleService) is not LocaleManager localeManager)
                    return Array.Empty<string>();

                var languageTags = localeManager.OverrideLocaleConfig?.SupportedLocales?.ToLanguageTags() ?? string.Empty;
                return languageTags.Split(',');
            }
            set
            {
                if (!IsSelectedCulturesSupported)
                    throw new NotSupportedException("AvailableCultures is not supported on this device.");

                if (Application.Context.GetSystemService(Context.LocaleService) is LocaleManager localeManager)
                {
                    localeManager.OverrideLocaleConfig = value.Length > 0
                        ? new LocaleConfig(LocaleList.ForLanguageTags(string.Join(",", value)))
                        : null;
                }
            }
        }

    }
}