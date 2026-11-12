using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.App;

/// <summary>Specifies the operating system the app runs on.</summary>
/// <remarks>
/// It is for what a store's policy or the product decides by platform, as an App Store build that may not point at
/// another platform's store (App Review guideline 2.3.10); what the device can do comes from
/// <see cref="DeviceIntentFeatures"/>.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AppOsType>))]
public enum AppOsType
{
    /// <summary>An operating system the app does not tell.</summary>
    Unknown,

    /// <summary>Windows.</summary>
    Windows,

    /// <summary>Linux.</summary>
    Linux,

    /// <summary>Android.</summary>
    Android,

    /// <summary>iOS, and tvOS.</summary>
    Ios,

    /// <summary>macOS.</summary>
    MacOs
}
