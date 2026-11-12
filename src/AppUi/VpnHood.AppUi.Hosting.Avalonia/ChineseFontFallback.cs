using Avalonia;
using Avalonia.Media;

namespace VpnHood.AppUi.Hosting.Avalonia;

// The face Chinese is drawn in, named where an OS would choose another. The words are Simplified, and
// the OS fills in every character the text font lacks, asked for the plain "zh". Windows takes that for
// no language at all: it picks a Japanese face, then a Traditional one for what that lacks, so a line
// mixes the two and its commas float in the middle. Linux's fontconfig answers with Noto Sans CJK's
// Japanese cut, so the shapes are Japanese; a system without the Simplified cut goes on as before.
// Android finds its Simplified face from "zh", and a browser page has only the one the store sends it.
// Apple finds PingFang SC, in regular for every weight; naming it would be worse, as the app's Light
// text turns thin and bold turns Light too: its heaviest face is 600, and for a weight it lacks Avalonia
// takes the nearest it has loaded. So only a desktop host calls this.
//
// A fallback rather than "zh-Hans" as the culture: Avalonia keeps the first face it finds for a script,
// and the language list has drawn Chinese by the time someone picks it, so a culture would take effect
// only at the next start. It holds in every language, the words having no other Chinese; kana stays
// out of it, so a Japanese name typed in only takes its kanji from here.

/// <summary>
/// Provides a font fallback that draws the Chinese characters and their punctuation in a Simplified Chinese face on
/// Windows and Linux.
/// </summary>
/// <remarks>Elsewhere it changes nothing, so only a desktop host calls it.</remarks>
public static class ChineseFontFallback
{
    // the punctuation with the ideographs: a comma comes from the face of the words around it
    private const string Range = "3000-303F, 3400-9FFF, F900-FAFF, FF00-FFEF";

    private static string? FamilyName =>
        OperatingSystem.IsWindows() ? "Microsoft YaHei UI" :
        OperatingSystem.IsLinux() ? "Noto Sans CJK SC" :
        null;

    /// <summary>
    /// Names Microsoft YaHei UI on Windows, or Noto Sans CJK SC on Linux, as the face of the Chinese characters and their
    /// punctuation; elsewhere it changes nothing.
    /// </summary>
    /// <param name="builder">The app builder.</param>
    /// <returns>The app builder.</returns>
    public static AppBuilder WithChineseFontFallback(this AppBuilder builder)
    {
        return FamilyName is not { } familyName
            ? builder
            : builder.With(new FontManagerOptions {
                FontFallbacks = [new FontFallback { FontFamily = new FontFamily(familyName), UnicodeRange = UnicodeRange.Parse(Range) }]
            });
    }
}
