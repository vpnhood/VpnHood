using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsVpnPlugin;

// Links and the clipboard by Windows' own brokered APIs, which the package's sandbox (an AppContainer) lets the app
// call: the shell execute behind Avalonia's launcher and the Win32 clipboard behind its clipboard are refused it. A
// read of the clipboard is refused either way - Windows' API reads only for a UWP window - so a paste is not to be
// had. On the UI thread, the window in front, as Windows' launcher wants of its caller.
internal sealed class WindowsVpnPluginDesktopShell : IDesktopShell
{
    public async Task<bool> OpenLink(Uri url, CancellationToken cancellationToken)
    {
        return await Launcher.LaunchUriAsync(url).AsTask(cancellationToken).Vhc();
    }

    public void CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush(); // the text stays once the app ends
    }
}
