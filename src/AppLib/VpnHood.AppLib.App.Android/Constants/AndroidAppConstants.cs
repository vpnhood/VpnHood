namespace VpnHood.AppLib.App.Android.Constants;

public static class AndroidAppConstants
{
    public const string? Label = "@string/app_name";
    public const string? Icon = "@mipmap/ic_launcher";
    public const string? Banner = "@drawable/ic_banner"; // for TV
    public const string NetworkSecurityConfig = "@xml/network_security_config"; // this library's: Let's Encrypt's root for Android 7.0, http to localhost
    public const string AdProcessName = ":AppAdProcess";
    public const bool SupportsRtl = true;
    public const bool AllowBackup = true;
}