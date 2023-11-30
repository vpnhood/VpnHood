using Android;

// In SDK-style projects such as this one, several assembly attributes that were historically
// defined in this file are now automatically added during build and populated with
// values defined in project properties. For details of which attributes are included
// and how to customise this process see: https://aka.ms/assembly-info-properties

[assembly: UsesPermission(Name = Manifest.Permission.Internet)]
[assembly: UsesPermission(Name = Manifest.Permission.QueryAllPackages)] // to exclude/include apps
[assembly: UsesPermission(Name = Manifest.Permission.PostNotifications)] // notification
[assembly: UsesPermission(Name = Manifest.Permission.ForegroundService)] // required for VPN
[assembly: UsesPermission(Name = Manifest.Permission.ForegroundServiceSystemExempted)] // required for VPN
[assembly: UsesPermission(Name = Manifest.Permission.StartForegroundServicesFromBackground)] // required for VPN
