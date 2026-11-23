using System.Diagnostics;
using VpnHood.Core.Client.Devices.Linux;

namespace VpnHood.AppLogic.Test.Tests;

// no test base: its helper starts a test web server with a Windows-only socket call, which Linux refuses
[TestClass]
public class LinuxVpnKillSwitchTest
{
    public required TestContext TestContext { get; set; }

    // A block whose table the kernel no longer ran, as after a reboot before its boot unit loaded it, or with no
    // systemd to load it, read as up from its boot copy, though nothing filtered. Only the kernel's table says so now.
    // A table and folders of the test's own, which no block of the machine's shares; the same table in the kernel reads
    // as up first, so a switch that reads no table at all, as with no nft, does not pass
    [TestMethod]
    public async Task A_block_with_only_its_boot_copy_is_not_up()
    {
        if (!OperatingSystem.IsLinux() || !Environment.IsPrivilegedProcess)
            Assert.Inconclusive("Linux as root only: the block's table is the kernel's.");

        var nftPath = LinuxVpnKillSwitch.FindNft() ??
                      throw new AssertInconclusiveException("With nftables only: the block's table is the kernel's.");

        var folderPath = Path.Combine(Path.GetTempPath(), "vh-kill-switch-test-" + Guid.NewGuid().ToString("N"));
        var tableName = "vh_test_" + Guid.NewGuid().ToString("N")[..12];
        var killSwitch = new LinuxVpnKillSwitch("linux-kill-switch-test") {
            TableName = tableName,
            RunFolder = Path.Combine(folderPath, "run"),
            BootFolder = Path.Combine(folderPath, "boot")
        };

        // the table in the kernel, tagged as the switch tags it, with no chain: it filters nothing
        var ruleset = $"table inet {tableName} {{\n  comment \"{killSwitch.OwnerTag}\"\n}}\n";
        Directory.CreateDirectory(killSwitch.BootFolder);
        try {
            var rulesetPath = Path.Combine(folderPath, "table.nft");
            await File.WriteAllTextAsync(rulesetPath, ruleset, TestContext.CancellationToken);
            try {
                await RunNft(nftPath, ["-f", rulesetPath], TestContext.CancellationToken);
                Assert.IsTrue(await killSwitch.IsBlocking(TestContext.CancellationToken),
                    "The kernel's table read as none.");

                await RunNft(nftPath, ["delete", "table", "inet", tableName], TestContext.CancellationToken);

                // its boot copy alone
                await File.WriteAllTextAsync(Path.Combine(killSwitch.BootFolder, "block.nft"), ruleset,
                    TestContext.CancellationToken);

                Assert.IsFalse(await killSwitch.IsBlocking(TestContext.CancellationToken));
            }
            finally {
                // a time of its own, as the test's end may be what brought it here; gone already where the test ran, and
                // a table left behind fails the test
                using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await RunNft(nftPath, ["delete", "table", "inet", tableName], cleanupCts.Token, throwOnError: false);
                var tables = await RunNft(nftPath, ["list", "tables"], cleanupCts.Token);
                if (tables.Contains($"inet {tableName}", StringComparison.Ordinal))
                    throw new InvalidOperationException($"The test's table {tableName} is left behind.");
            }
        }
        finally {
            Directory.Delete(folderPath, recursive: true);
        }
    }

    // nft with its arguments as given, a path with spaces too; one whose wait ends is killed and waited for, so none
    // outlives the test
    // nft's output
    private static async Task<string> RunNft(string nftPath, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken, bool throwOnError = true)
    {
        var startInfo = new ProcessStartInfo(nftPath) {
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("nft did not start.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        try {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) {
            process.Kill();
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        if (throwOnError && process.ExitCode != 0)
            throw new InvalidOperationException(
                $"nft {string.Join(' ', arguments)} failed: {await process.StandardError.ReadToEndAsync(cancellationToken)}");

        return await output;
    }
}
