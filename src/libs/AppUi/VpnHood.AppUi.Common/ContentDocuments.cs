using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.AppUi.Common;

// The long-form documents of the UI's store (content/<lang>/<name>.md), the counterpart of the web
// UI's ContentDocuments.ts: reading one is this class's job, and what a caller gets back is the
// finished text. The product's own names are placeholders in the file - {appName}, {companyName} -
// so one document serves every head and a fork's alike, and they are filled HERE, as the file is
// read, not where a view draws it: the document exists in this app only in its filled form, front
// matter included, and no page can forget to do it. A name is text, whatever characters it
// carries: the markdown renderer treats it as prose.
// The translator never touches a placeholder - vhtranslator's docs mode masks {...} out of the
// body before a model reads it (maskPatterns) and puts it back after, so the words around it are
// translated and the placeholder is restored, not retyped.

/// <summary>
/// Provides the long-form documents of the UI's content store, with the product's names filled in.
/// </summary>
public static class ContentDocuments
{
    private const string SourceLanguage = "en";

    /// <summary>
    /// Loads a document in the app's language, else in its language alone, else in English, with the product's names
    /// filled in.
    /// </summary>
    /// <param name="assets">The UI's content store.</param>
    /// <param name="name">The document's name.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the document's text.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="InvalidOperationException">
    /// The store has no such document, not even in English, or the app's state has not been read (<see cref="VhApp.Init"/>).
    /// </exception>
    public static async Task<string> LoadAsync(IAssetProvider assets, string name,
        CancellationToken cancellationToken)
    {
        var culture = VhApp.State.CurrentUiCultureInfo.Code;
        foreach (var language in new[] { culture, culture.Split('-')[0], SourceLanguage }) {
            var text = await ReadAsync(assets, $"content/{language}/{name}.md", cancellationToken).ConfigureAwait(false);
            if (text != null)
                return FillPlaceholders(text);
        }

        throw new InvalidOperationException(
            $"The asset store has no content document '{name}' for '{SourceLanguage}'.");
    }

    private static async Task<string?> ReadAsync(IAssetProvider assets, string assetPath,
        CancellationToken cancellationToken)
    {
        await using var stream = await assets.TryOpenReadAsync(assetPath, cancellationToken).ConfigureAwait(false);
        if (stream == null)
            return null;

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string FillPlaceholders(string text)
    {
        var features = VhApp.Descriptor;
        return text
            .Replace("{appName}", features.AppName)
            .Replace("{companyName}", features.CompanyName);
    }
}
