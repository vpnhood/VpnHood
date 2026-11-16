using Android;

// The app installs its own update. Only the heads handed out outside the stores reference this
// library: Google Play forbids this permission for an app's own updates.
[assembly: UsesPermission(Name = Manifest.Permission.RequestInstallPackages)]
