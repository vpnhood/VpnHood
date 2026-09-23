namespace VpnHood.Core.Toolkit.Assets;

// No asset by that name. A type rather than a message every provider writes for itself, so the
// wording cannot drift and a caller that is only asking can catch THIS and nothing else - a file
// that exists but will not open is a different failure and must not be mistaken for an absent one.
public class AssetNotFoundException(string assetPath, Exception? innerException = null)
    : FileNotFoundException(
        $"There is no asset '{assetPath}'. Whatever places this build's data files has not placed it.",
        assetPath, innerException);
