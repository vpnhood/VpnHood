using VpnHood.Core.Client.VpnServices.Host.Linux;
using VpnHood.Test.Device;

namespace VpnHood.AppLogic.Test.Tests;

// no test base: its helper starts a test web server with a Windows-only socket call, which Linux refuses
[TestClass]
public class LinuxVpnTunnelLockTest
{
    public required TestContext TestContext { get; set; }

    // The lock made its folder its owner's alone only when it made it: one another tool had left open was taken as it
    // was. Its mode is set at each take. A folder of the test's own, so the machine's tunnel is neither read nor taken
    [TestMethod]
    public async Task The_lock_keeps_its_folder_its_owners_alone()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Inconclusive("Linux only: the lock is flock's, and its folder's mode Unix's.");

        const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        var folderPath = Path.Combine(Path.GetTempPath(), "vh-tunnel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folderPath);
        File.SetUnixFileMode(folderPath, ownerOnly | UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                                         UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite |
                                         UnixFileMode.OtherExecute);

        try {
            // the take itself ends at the deadline, so none is left running after the test
            using var takeCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            takeCts.CancelAfter(TimeSpan.FromSeconds(10));
            using var hold = await new LinuxVpnTunnelLock(folderPath).Take(new RecordingVpnTunnelHolder(),
                takeCts.Token);

            Assert.AreEqual(ownerOnly, File.GetUnixFileMode(folderPath));
        }
        finally {
            Directory.Delete(folderPath, recursive: true);
        }
    }

    // A take whose token was canceled took a free lock all the same, by the path that waits for nothing, so a connect
    // a stop had canceled held the machine's tunnel. It takes nothing
    [TestMethod]
    public async Task A_canceled_take_takes_no_free_lock()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Inconclusive("Linux only: the lock is flock's.");

        var folderPath = Path.Combine(Path.GetTempPath(), "vh-tunnel-test-" + Guid.NewGuid().ToString("N"));
        try {
            var tunnelLock = new LinuxVpnTunnelLock(folderPath);
            using var canceledCts = new CancellationTokenSource();
            await canceledCts.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                tunnelLock.Take(new RecordingVpnTunnelHolder(), canceledCts.Token));

            // the lock is still free: a take finds no holder to ask
            using var takeCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            takeCts.CancelAfter(TimeSpan.FromSeconds(10));
            using var hold = await tunnelLock.Take(new RecordingVpnTunnelHolder(), takeCts.Token);
        }
        finally {
            if (Directory.Exists(folderPath))
                Directory.Delete(folderPath, recursive: true);
        }
    }
}
