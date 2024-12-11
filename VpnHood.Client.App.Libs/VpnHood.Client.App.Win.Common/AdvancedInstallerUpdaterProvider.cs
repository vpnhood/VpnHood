using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App.Win.Common;

public class AdvancedInstallerUpdaterProvider : IAppUpdaterProvider
{
    // return false if the app update system does not work
    public async Task<bool> Update(IUiContext uiContext)
    {
        // launch updater if exists
        var assemblyLocation = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ??
                               throw new Exception("Could not get the parent of Assembly location.");

        var updaterFilePath = Path.Combine(assemblyLocation, "updater.exe");
        if (!File.Exists(updaterFilePath))
            throw new Exception($"Could not find updater: {updaterFilePath}.");

        // check for update
        VhLogger.Instance.LogInformation("Checking for new updates...");
        var process = Process.Start(updaterFilePath, "/justcheck");
        if (process == null) return false;
        while (process is { HasExited: false })
            await Task.Delay(500).VhConfigureAwait();

        // install update
        if (process.ExitCode == 0) {
            process = Process.Start(updaterFilePath);
            if (process == null) return false;
            while (process is { HasExited: false })
                await Task.Delay(500).VhConfigureAwait();
        }

        // https://www.advancedinstaller.com/user-guide/updater.html#updater-return-codes
        return process.ExitCode is 0 or -536870895;
    }
}