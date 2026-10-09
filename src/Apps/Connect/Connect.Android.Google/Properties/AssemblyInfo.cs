// In SDK-style projects such as this one, several assembly attributes that were historically
// defined in this file are now automatically added during build and populated with
// values defined in project properties. For details of which attributes are included
// and how to customise this process see: https://aka.ms/assembly-info-properties

[assembly: UsesFeature("android.software.leanback", Required = false)]
[assembly: UsesFeature("android.hardware.touchscreen", Required = false)]

#if DEBUG
// A Debug build runs Firebase as Release does but sends nothing. These hold from a fresh install. Where
// a Release build ran before, Crashlytics' saved choice outweighs its flag until the head turns the
// tracker off (App.OnCreate), which applies from the next start.
[assembly: MetaData("firebase_analytics_collection_deactivated", Value = "true")]
[assembly: MetaData("firebase_crashlytics_collection_enabled", Value = "false")]
#endif

// [assembly: UsesPermission(Name = "android.permission.ACCESS_WIFI_STATE")] // InMobi
// [assembly: UsesPermission(Name = "android.permission.CHANGE_WIFI_STATE")] // InMobi
// [assembly: UsesPermission(Name = "com.google.android.gms.permission.AD_ID")] // InMobi