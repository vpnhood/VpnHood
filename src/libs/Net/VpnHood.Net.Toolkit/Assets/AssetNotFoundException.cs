namespace VpnHood.Net.Toolkit.Assets;

/// <summary>
/// The exception that is thrown when there is no asset at a path. A type of its own, so the wording cannot drift and a
/// caller that only asks can catch this and nothing else: a file that exists but will not open is a different failure.
/// </summary>
/// <param name="assetPath">The asset's path, also the exception's <see cref="FileNotFoundException.FileName"/>.</param>
/// <param name="innerException">The exception that is the cause of this one, or <see langword="null"/>.</param>
public class AssetNotFoundException(string assetPath, Exception? innerException = null)
    : FileNotFoundException(
        $"There is no asset '{assetPath}'. Whatever places this build's data files has not placed it.",
        assetPath, innerException);
