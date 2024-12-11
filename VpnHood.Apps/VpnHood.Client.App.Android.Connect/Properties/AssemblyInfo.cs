// In SDK-style projects such as this one, several assembly attributes that were historically
// defined in this file are now automatically added during build and populated with
// values defined in project properties. For details of which attributes are included
// and how to customise this process see: https://aka.ms/assembly-info-properties

[assembly: UsesFeature("android.software.leanback", Required = false)]
[assembly: UsesFeature("android.hardware.touchscreen", Required = false)]

[assembly: UsesPermission(Name = "android.permission.INTERNET")]
[assembly: UsesPermission(Name = "android.permission.ACCESS_NETWORK_STATE")]

[assembly: UsesPermission(Name = "android.permission.ACCESS_WIFI_STATE")] // InMobi
[assembly: UsesPermission(Name = "android.permission.CHANGE_WIFI_STATE")] // InMobi
[assembly: UsesPermission(Name = "com.google.android.gms.permission.AD_ID")] // InMobi
