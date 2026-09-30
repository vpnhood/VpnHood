using VpnHood.AppLib.App;
using VpnHood.Core.Common.Exceptions;

namespace VpnHood.AppLib.Test.Tests;

// The single-instance lock as a file: a second holder is another instance until the first lets go.
[TestClass]
public class InstanceLockFileTest
{
    [TestMethod]
    public void A_second_take_is_another_instance_until_the_first_lets_go()
    {
        const string appId = "com.vpnhood.test.lock";
        // straight under the temp folder: a folder another user's run left there may not be ours to write in
        var folderPath = Path.Combine(Path.GetTempPath(), "vhtest-lock-" + Guid.CreateVersion7());
        try {
            var first = InstanceLockFile.Take(folderPath, appId);
            Assert.ThrowsExactly<AnotherInstanceIsRunningException>(() => InstanceLockFile.Take(folderPath, appId));

            first.Dispose();
            using var second = InstanceLockFile.Take(folderPath, appId);

            // a lock anyone may read is one anyone may hold
            if (!OperatingSystem.IsWindows())
                Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    File.GetUnixFileMode(Path.Combine(folderPath, InstanceLockFile.FileName)));
        }
        finally {
            if (Directory.Exists(folderPath))
                Directory.Delete(folderPath, recursive: true);
        }
    }
}
