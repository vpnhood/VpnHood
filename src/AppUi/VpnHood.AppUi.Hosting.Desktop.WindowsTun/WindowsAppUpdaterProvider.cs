using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.Desktop.Updaters;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsTun;

// Install on an install from our site runs the downloaded setup silently, as a person's run does: it stops this
// service, closes what runs from the install's folder - the window and the tray among them, which, as on Android, do
// not come back by themselves - puts the new version in, and starts the service again. The setup outlives this
// service, its parent: Windows ends no process with the one that started it.
public sealed class WindowsAppUpdaterProvider : DesktopAppUpdaterProvider
{
    private WindowsAppUpdaterProvider(string storagePath, Uri updateInfoUrl)
        : base(storagePath, updateInfoUrl)
    {
    }

    /// <summary>
    /// The updater of an install the setup made, whose version's folder holds the setup's publish.json; null for a
    /// copy run where it lies, a debugger's build.
    /// </summary>
    public static WindowsAppUpdaterProvider? TryCreate(AppOptionsContext context)
    {
        if (new WindowsDesktopPaths(context.AppId).InstallRootPath is null ||
            ReadUpdateInfoUrl(Path.Combine(AppContext.BaseDirectory, "publish.json")) is not { } updateInfoUrl)
            return null;

        return new WindowsAppUpdaterProvider(context.StoragePath, updateInfoUrl);
    }

    protected override async Task<string?> RunInstaller(string packagePath)
    {
        var startInfo = new ProcessStartInfo { FileName = packagePath, UseShellExecute = false };
        startInfo.ArgumentList.Add("/S");

        VhLogger.Instance.LogInformation("Installing the update. Setup: {Setup}", packagePath);
        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException("Could not run the setup.");
        await process.WaitForExitAsync().Vhc();

        // the setup stops this service before it puts anything in, so one that ends first installed nothing
        return $"The setup ended with code {process.ExitCode}.";
    }
}
