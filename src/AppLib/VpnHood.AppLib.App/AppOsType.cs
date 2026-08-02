using System.Text.Json.Serialization;

namespace VpnHood.AppLib;

// The OS the app process is running on, published to the SPA through AppFeatures.
//
// The SPA needs this because some content is only legal//useful on certain platforms: an App Store
// build may not point at another mobile platform's store or advertise a download it cannot offer
// (App Review guideline 2.3.10), while the same screen is genuinely useful elsewhere. Capability
// questions ("can this device pin a quick-launch tile?") must keep using the IDeviceUiProvider
// flags — this is for the store-policy/product cases those flags cannot express.
[JsonConverter(typeof(JsonStringEnumConverter<AppOsType>))]
public enum AppOsType
{
    Unknown,
    Windows,
    Linux,
    Android,
    Ios,
    MacOs
}
