namespace VpnHood.AppLib.Droid.Common.Constants;

public static class AndroidAppConstants
{
    public const string? Label = "@string/app_name";
    public const string? Icon = "@mipmap/appicon";
    public const string? Banner = "@mipmap/banner"; // for TV
    public const string NetworkSecurityConfig = "@xml/network_security_config"; // required for localhost
    public const string AdProcessName = ":AppAdProcess";
    public const bool SupportsRtl = true;
    public const bool AllowBackup = true;
}