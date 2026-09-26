using Foundation;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Devices.Ios;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.Ios;

// The app on iOS, from the same init params as every platform plus the two iOS facts
// (IosInitParams). The Network Extension is a binary of its own and never runs this.
public class VpnHoodIosApp : Singleton<VpnHoodIosApp>
{
    public static VpnHoodIosApp Init(IosInitParams initParams)
    {
        // do not init again
        if (IsInit)
            return Instance;

        if (VpnHoodApp.IsInit)
            return new VpnHoodIosApp();

        // the app's process has a readable stdout, so a console logger is right here
        VhLogger.Instance = VhLogger.CreateConsoleLogger();

        // the bundle is a folder, and the asset packages placed their files in it
        var context = new AppOptionsContext {
            AppId = initParams.AppId,
            StoragePath = initParams.ResolveStoragePath(),
            PackagedAssetProvider = new FolderAssetProvider(AppContext.BaseDirectory)
        };

        var options = initParams.AppOptionsFactory(context);
        options.DeviceUiProvider ??= new IosDeviceUiProvider();
        options.CultureProvider ??= IosAppCultureProvider.CreateIfSupported();

        VpnHoodApp.Init(CreateDevice(initParams, options.AppName), options);
        return new VpnHoodIosApp();
    }

    // Resolved here - on the main thread, after iOS has set up the sandbox - so the App Group's
    // container path, the app's and the extension's IPC folder, is stable for the whole session.
    private static IosDevice CreateDevice(IosInitParams initParams, string appName)
    {
        var sharedContainerPath = NSFileManager.DefaultManager.GetContainerUrl(initParams.AppGroupId)?.Path
            ?? throw new InvalidOperationException(
                $"The App Group {initParams.AppGroupId} has no container: add it to the entitlements and the " +
                "provisioning profiles of both the app and its extension, which share the VPN service's folder there.");
        VhLogger.Instance.LogInformation("GetContainerUrl({AppGroupId}) = {Path}",
            initParams.AppGroupId, sharedContainerPath);

        return new IosDevice(
            providerBundleId: initParams.ProviderBundleId,
            sharedContainerPath: sharedContainerPath,
            localizedDescription: appName);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            if (VpnHoodApp.IsInit)
                VpnHoodApp.Instance.Dispose();
        }

        base.Dispose(disposing);
    }
}
