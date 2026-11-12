using VpnHood.AppUi.Common;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.AppUi.Hosting.Avalonia;

//

/// <summary>Defines a UI a head can run: the head names it as a type argument.</summary>
/// <remarks>The members are static, as a head needs both answers before the UI's first view, with no instance of it.</remarks>
public interface IAvaloniaUi
{
    /// <summary>
    /// Gets the languages this UI has words for, which the app is told as it is configured, so its language list and its
    /// choice of culture come from the words that exist.
    /// </summary>
    static abstract IReadOnlyList<string> AvailableCultures { get; }

    /// <summary>
    /// Puts in place what the UI needs before its first view, its words and the fonts its styles name, from the content
    /// store the head hands in.
    /// </summary>
    /// <param name="assets">The UI's content store: a folder in process, or the app's web host from a browser page.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    static abstract Task PrepareContentAsync(IAssetProvider assets, CancellationToken cancellationToken);
}
