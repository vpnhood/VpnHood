namespace VpnHood.Core.Toolkit.Assets;

// Assets under a folder, read where they lie. Which folder is the caller's to say - an app passes
// AppContext.BaseDirectory, which is the folder beside the executable on Windows and Linux and the
// app bundle on iOS, tvOS and Mac Catalyst - so nothing here assumes anything about who is reading
// or what the files are for.
//
// This is every platform whose placed assets are ordinary files. Android is not: its assets live
// inside the package, and its own provider reads them there.
public class FolderAssetProvider(string folderPath) : IAssetProvider
{
    public Stream OpenRead(string assetPath)
    {
        var path = Path.Combine(folderPath, assetPath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path)
            ? File.OpenRead(path)
            : throw new FileNotFoundException(
                $"There is no asset '{assetPath}' under '{folderPath}'. An asset package's build targets " +
                "place its files; a build that excludes that package's build assets places nothing.", path);
    }
}
