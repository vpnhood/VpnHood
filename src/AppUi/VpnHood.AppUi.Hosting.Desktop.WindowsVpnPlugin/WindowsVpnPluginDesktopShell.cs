using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Notifications;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsVpnPlugin;

// Links, the clipboard and the taskbar button's badge by Windows' own brokered APIs, which the package's sandbox (an
// AppContainer) lets the app call: the shell execute behind Avalonia's launcher, the Win32 clipboard behind its
// clipboard and the host's overlay icon are refused it. A read of the clipboard is refused either way - Windows' API
// reads only for a UWP window - so a paste is not to be had. On the UI thread, the window in front, as Windows'
// launcher wants of its caller.
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

    // Windows' badge glyphs nearest the website edition's overlay - a green dot, an amber one, a red one with a
    // cross - drawn by Windows on the button's corner where the overlay goes. The badge is the app's, not a
    // window's: a pinned button keeps it after the app ends, unless cleared.
    public void ShowConnectionBadge(DesktopConnectionBadge? badge)
    {
        var updater = BadgeUpdateManager.CreateBadgeUpdaterForApplication();
        if (badge is not { } value) {
            updater.Clear();
            return;
        }

        var glyph = value switch {
            DesktopConnectionBadge.Connected => "available",
            DesktopConnectionBadge.Connecting => "away",
            _ => "error"
        };
        var xml = BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeGlyph);
        xml.DocumentElement.SetAttribute("value", glyph);
        updater.Update(new BadgeNotification(xml));
    }
}
