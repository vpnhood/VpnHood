using VpnHood.Core.Server;

namespace VpnHood.Test.Tests.Servers;

[TestClass]
public class DownloadServiceTest : TestBase
{
    // The folder was checked as a bare prefix of the file's full path, so a request could climb into a sibling folder
    // whose name begins with the folder's
    [TestMethod]
    public void A_download_stays_inside_its_folder()
    {
        var downloadsPath = Path.Combine(TestHelper.WorkingPath, "downloads");
        var privatePath = Path.Combine(TestHelper.WorkingPath, "downloads-private");
        Directory.CreateDirectory(downloadsPath);
        Directory.CreateDirectory(privatePath);
        File.WriteAllText(Path.Combine(downloadsPath, "file.txt"), "public");
        File.WriteAllText(Path.Combine(privatePath, "secret.txt"), "secret");

        Assert.IsNotNull(DownloadService.ResolveFilePath(downloadsPath, "/downloads/file.txt", out _, out _));
        Assert.IsNull(DownloadService.ResolveFilePath(downloadsPath, "/downloads/../downloads-private/secret.txt",
            out _, out _));
    }
}
