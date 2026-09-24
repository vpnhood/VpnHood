using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Extensions;

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
public static class ContentDocuments
{
    private const string SourceLanguage = "en";

    // The document in the app's language, else in English: a language whose translation failed
    // verification ships no file, and the English text beats none - on a consent screen above all.
    // Read through the store's provider, which may be a web server, so it lands after the page shows.
    public static async Task<string> LoadAsync(IAssetProvider assets, string name,
        CancellationToken cancellationToken)
    {
        var culture = VhApp.State.CurrentUiCultureInfo.Code;
        foreach (var language in new[] { culture, culture.Split('-')[0], SourceLanguage }) {
            var text = await ReadAsync(assets, $"content/{language}/{name}.md", cancellationToken).Vhc();
            if (text != null)
                return FillPlaceholders(text);
        }

        throw new InvalidOperationException(
            $"The asset store has no content document '{name}' for '{SourceLanguage}'.");
    }

    private static async Task<string?> ReadAsync(IAssetProvider assets, string assetPath,
        CancellationToken cancellationToken)
    {
        await using var stream = await assets.TryOpenReadAsync(assetPath, cancellationToken).Vhc();
        if (stream == null)
            return null;

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken).Vhc();
    }

    private static string FillPlaceholders(string text)
    {
        var features = VhApp.Features;
        return text
            .Replace("{appName}", features.AppName)
            .Replace("{companyName}", features.CompanyName);
    }
}
