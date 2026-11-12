namespace VpnHood.AppLib.App.Android.Constants;

/// <summary>Provides the values a head's application attribute takes.</summary>
public static class AndroidAppConstants
{
    /// <summary>The app's label: its name.</summary>
    public const string? Label = "@string/app_name";

    /// <summary>The app's icon.</summary>
    public const string? Icon = "@mipmap/ic_launcher";

    /// <summary>The app's banner, for a TV's launcher.</summary>
    public const string? Banner = "@drawable/ic_banner"; // for TV

    /// <summary>The network security configuration this library ships: Let's Encrypt's roots for older Androids, and http to localhost.</summary>
    public const string NetworkSecurityConfig = "@xml/network_security_config"; // this library's: Let's Encrypt's roots for older Androids, http to localhost
    internal const string AdProcessName = ":AppAdProcess";

    /// <summary>Whether the app lays out right to left for such languages: yes.</summary>
    public const bool SupportsRtl = true;

    /// <summary>Whether Android may back the app's data up: yes.</summary>
    public const bool AllowBackup = true;
}