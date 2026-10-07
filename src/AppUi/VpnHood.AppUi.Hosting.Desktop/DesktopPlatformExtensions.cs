namespace VpnHood.AppUi.Hosting.Desktop;

internal static class DesktopPlatformExtensions
{
    extension(IDesktopPlatform platform)
    {
        // What a person who may not use the app reads: the service's refusal, and the commands' own.
        public string AdministratorsOnlyMessage =>
            $"Only administrators can use {platform.Paths.InstanceName} on this computer.";
    }
}
