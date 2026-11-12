namespace VpnHood.AppUi.Hosting.Desktop.Abstractions;

/// <summary>
/// Defines what the UI asks of the system outside its window, where the UI framework's own way, or the host's, is
/// refused.
/// </summary>
/// <remarks>
/// It serves where the platform keeps an app from those ways, as for the Store edition. A host with no shell leaves the
/// framework's and the host's own ways (<see cref="DesktopUiParams.Shell"/>).
/// </remarks>
public interface IDesktopShell
{
    /// <summary>
    /// Opens a link with the application the system associates with it: a web link in the person's browser.
    /// </summary>
    /// <param name="url">The link.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task whose result is <see langword="true"/> where the link opened, or <see langword="false"/> where the system
    /// would not open it, and the UI offers it another way.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<bool> OpenLink(Uri url, CancellationToken cancellationToken);

    /// <summary>Puts text on the clipboard, for the person to paste elsewhere.</summary>
    /// <param name="text">The text.</param>
    void CopyText(string text);

    /// <summary>Shows the connection's state on the app's taskbar button, in the system's own badge.</summary>
    /// <param name="badge">The state, or <see langword="null"/> to take the badge away, as the UI ends.</param>
    void ShowConnectionBadge(DesktopConnectionBadge? badge);
}
