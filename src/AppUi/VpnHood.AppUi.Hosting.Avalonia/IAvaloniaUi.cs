using VpnHood.AppLib.Api;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.AppUi.Hosting.Avalonia;

/// <summary>Defines a UI a head can run: the head names it as a type argument.</summary>
/// <remarks>
/// The members are static, as the first of them runs before Avalonia starts, when there is no instance of the UI yet. A
/// host runs them in this order, all before the UI's first view: <see cref="InitAsync"/>, <see cref="PrepareContentAsync"/>,
/// then <see cref="ConfigureAsync"/>. A host with no app to give the UI, as a desktop's message window, runs
/// <see cref="PrepareContentAsync"/> alone.
/// </remarks>
public interface IAvaloniaUi
{
    /// <summary>
    /// Gives the UI the app's API, which the UI reads the app's information through: before Avalonia starts, as the
    /// features decide the theme.
    /// </summary>
    /// <param name="api">The app's API: the app's own in process, or the same six interfaces over HTTP from a browser page.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    static abstract Task InitAsync(VpnHoodApi api, CancellationToken cancellationToken);

    /// <summary>
    /// Puts in place what the UI needs before its first view, its words and the fonts its styles name, from the content
    /// store the head hands in. It needs no app.
    /// </summary>
    /// <param name="assets">The UI's content store: a folder in process, or the app's web host from a browser page.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    static abstract Task PrepareContentAsync(IAssetProvider assets, CancellationToken cancellationToken);

    /// <summary>
    /// Configures the app for the UI once its content is prepared: tells the app which languages the UI has words for, so
    /// its language list and its choice of culture come from the words that exist, and gives the app the UI's words for
    /// its own text, again whenever the UI's language changes.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    static abstract Task ConfigureAsync(CancellationToken cancellationToken);
}
