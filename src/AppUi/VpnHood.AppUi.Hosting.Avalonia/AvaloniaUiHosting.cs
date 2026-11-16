using VpnHood.AppLib.Api;
using VpnHood.AppUi.Hosting.Common;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.AppUi.Hosting.Avalonia;

// An in-process host waits for it on the thread that called, deliberately, rather than on a worker:
// on Android that is the activity's, where Avalonia is already up, and a UI that puts its fonts in
// place does it there. Blocking a thread that has a synchronization context is safe here because
// everything it waits for configures away from it (TaskExtensions.Vhc) - the rule this repo's async
// code already follows, and the one to keep when adding to this path. In process every step
// completes at once.

/// <summary>
/// Provides the Avalonia UI's start, for every host: the app's API handed to the UI, then the UI's content, its words
/// and its fonts, and the app configured for the UI.
/// </summary>
/// <remarks>
/// A desktop window, an iOS scene and a browser page run it whole (<see cref="StartAsync{TUi}"/>). Android runs it in
/// two halves: the API from the process's application, before Avalonia initializes, as the features it reads decide
/// the theme (<see cref="InitAsync{TUi}"/>); the rest from the activity, before it makes the view
/// (<see cref="PrepareAsync{TUi}"/>). An in-process host may wait for it on the calling thread, a UI thread included,
/// where the UI's own content preparation does not need that thread to complete. The host's lifetime and its UI context
/// stay the host's.
/// </remarks>
public static class AvaloniaUiHosting
{
    private static string? _appName;
    private static string? _uiTheme;

    /// <summary>Starts the UI whole: hands it the app's API, then prepares its content.</summary>
    /// <typeparam name="TUi">The UI's application.</typeparam>
    /// <param name="api">The app's API: the app's own in process, or the same six interfaces over HTTP from a browser page.</param>
    /// <param name="uiAssetProvider">The UI's content store, which the UI reads its pictures and its words from.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, or answered with an error it
    /// does not name.
    /// </exception>
    /// <exception cref="InvalidOperationException"><paramref name="uiAssetProvider"/> is <see langword="null"/>.</exception>
    public static async Task StartAsync<TUi>(VpnHoodApi api, IAssetProvider? uiAssetProvider,
        CancellationToken cancellationToken)
        where TUi : IAvaloniaUi
    {
        await InitAsync<TUi>(api, cancellationToken).ConfigureAwait(false);
        await PrepareAsync<TUi>(uiAssetProvider, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Hands the UI the app's API, and reads the app's information: the first half of <see cref="StartAsync{TUi}"/>.
    /// </summary>
    /// <typeparam name="TUi">The UI's application.</typeparam>
    /// <param name="api">The app's API: the app's own in process, or the same six interfaces over HTTP from a browser page.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or, over HTTP, a request timed out.
    /// </exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Over HTTP, the app could not be reached.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, or answered with an error it
    /// does not name.
    /// </exception>
    public static async Task InitAsync<TUi>(VpnHoodApi api, CancellationToken cancellationToken)
        where TUi : IAvaloniaUi
    {
        await TUi.InitAsync(api, cancellationToken).ConfigureAwait(false);

        // the host's own read, of only what stays as it is while the app runs: the TV layout, which does not, is the
        // UI's to say
        var info = await api.App.GetInfo(cancellationToken).ConfigureAwait(false);
        _uiTheme = info.Descriptor.UiTheme;
        _appName = info.Descriptor.AppName;
    }

    /// <summary>Gets the app's name, as the app's information gave it when the UI was started.</summary>
    /// <exception cref="InvalidOperationException">The UI has not been started (<see cref="InitAsync{TUi}"/>).</exception>
    public static string AppName => _appName ?? throw NotStartedException();

    // Avalonia's designer makes the UI with no app: no view is made then.
    internal static bool IsInit => _appName != null;

    // The view is made only once the UI's content is in place and the app is configured for it.
    internal static bool IsConfigured { get; private set; }

    // The colours of the OS chrome around the UI, its theme's (ThemeColors), as PrepareAsync read them
    // from the UI's store; the system's until then.
    internal static ThemeColors ThemeColors { get; private set; } = new();

    /// <summary>
    /// Gets or sets the system's ways for links and the clipboard where Avalonia's own are refused, as in a sandboxed app,
    /// which a desktop host names before the UI starts; or <see langword="null"/> for Avalonia's own.
    /// </summary>
    public static IDesktopShell? Shell { get; set; }

    /// <summary>
    /// Gets or sets how a text file, by its name and its text, is handed to the person through a browser page's downloads,
    /// so it lands on the device in their hand; the browser host names it, and it is <see langword="null"/> anywhere else.
    /// </summary>
    public static Action<string, string>? DownloadText { get; set; }

    /// <summary>
    /// Gets or sets how a browser page on plain http puts text on the clipboard, where the browser gives Avalonia's own way
    /// none; the browser host names it, and it is <see langword="null"/> anywhere else.
    /// </summary>
    public static Action<string>? CopyText { get; set; }

    /// <summary>
    /// Prepares the UI's content and its theme's colours, then has the UI configure the app for it: the second half of
    /// <see cref="StartAsync{TUi}"/>.
    /// </summary>
    /// <typeparam name="TUi">The UI's application.</typeparam>
    /// <param name="uiAssetProvider">The UI's content store, which the UI reads its pictures and its words from.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, or answered with an error it
    /// does not name.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="uiAssetProvider"/> is <see langword="null"/>, or <see cref="InitAsync{TUi}"/> has not completed, so
    /// the UI has no app yet.
    /// </exception>
    public static async Task PrepareAsync<TUi>(IAssetProvider? uiAssetProvider, CancellationToken cancellationToken)
        where TUi : IAvaloniaUi
    {
        var assets = uiAssetProvider ?? throw new InvalidOperationException(
            "The head has named no AppOptions.UiZipAssets, and this UI reads its pictures and its words from them.");
        var uiTheme = _uiTheme ?? throw NotStartedException();

        await TUi.PrepareContentAsync(assets, cancellationToken).ConfigureAwait(false);
        ThemeColors = await ThemeColors.LoadAsync(assets, uiTheme, cancellationToken).ConfigureAwait(false);
        await TUi.ConfigureAsync(cancellationToken).ConfigureAwait(false);
        IsConfigured = true;
    }

    private static InvalidOperationException NotStartedException()
    {
        return new InvalidOperationException(
            $"The UI has not been started. A head must call {nameof(AvaloniaUiHosting)}.{nameof(InitAsync)} first.");
    }
}
