namespace VpnHood.AppUi.Hosting.Desktop.Abstractions;

// What the UI asks of the OS outside its window, where the UI framework's own way, or the host's, is refused: a
// sandboxed app's (the Store edition's), which Windows lets neither shell-execute a link, reach the Win32 clipboard nor
// set its taskbar button's overlay icon. A host with none leaves the framework's and the host's own ways
// (DesktopUiParams.Shell).
public interface IDesktopShell
{
    // The link opened in the person's browser; false when the OS would not, and the UI offers it another way.
    Task<bool> OpenLink(Uri url, CancellationToken cancellationToken);

    // The text put on the clipboard, for the person to paste elsewhere.
    void CopyText(string text);

    // The connection's state on the app's taskbar button, in the OS's own badge; null takes it away, as the UI ends.
    void ShowConnectionBadge(DesktopConnectionBadge? badge);
}
