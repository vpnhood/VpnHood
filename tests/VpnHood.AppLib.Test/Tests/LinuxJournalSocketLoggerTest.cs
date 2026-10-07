using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App.Linux;

namespace VpnHood.AppLib.Test.Tests;

// The window's journal sink on Linux: the entry it sends, and that entry as the journal files it.
[TestClass]
public class LinuxJournalSocketLoggerTest
{
    private static readonly TimeSpan JournalWait = TimeSpan.FromSeconds(5);

    [TestMethod]
    public void An_entry_is_in_the_journals_native_form_with_its_lines_kept()
    {
        const string message = "The window could not reach the service.\nException: none";
        var entry = LinuxJournalSocketLogger.CreateEntry("VpnHoodTest", LogLevel.Warning, message);

        var fields = "PRIORITY=4\nSYSLOG_IDENTIFIER=VpnHoodTest\nMESSAGE\n"u8.ToArray();
        CollectionAssert.AreEqual(fields, entry[..fields.Length]);
        var messageLength = (int)BinaryPrimitives.ReadUInt64LittleEndian(entry.AsSpan(fields.Length, sizeof(ulong)));
        var messageStart = fields.Length + sizeof(ulong);
        Assert.AreEqual(message, Encoding.UTF8.GetString(entry, messageStart, messageLength));
        Assert.AreEqual(messageStart + messageLength + 1, entry.Length, "the message ends the entry");
        Assert.AreEqual((byte)'\n', entry[^1]);
    }

    [TestMethod]
    public async Task A_warning_reaches_the_journal_whole_and_an_information_does_not()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/run/systemd/journal/socket"))
            Assert.Inconclusive("Linux with systemd only: the entry goes to the journal's socket.");

        var identifier = $"VpnHoodTest{Environment.ProcessId}";
        using (var provider = new LinuxJournalSocketLoggerProvider(identifier)) {
            var logger = provider.CreateLogger("test");
            logger.LogInformation("Not for the journal.");
            logger.LogWarning("The first line.\nThe second line.");
        }

        var entries = await ReadJournal(identifier, count: 1);
        Assert.AreEqual(1, entries.Count, "the warning alone");
        Assert.AreEqual("The first line.\nThe second line.", entries[0]["MESSAGE"].GetString());
        Assert.AreEqual("4", entries[0]["PRIORITY"].GetString());
    }

    // The entries the journal has under the identifier, waited for: journald takes a datagram in its
    // own time.
    private static async Task<IReadOnlyList<Dictionary<string, JsonElement>>> ReadJournal(string identifier, int count)
    {
        var deadline = DateTime.Now + JournalWait;
        while (true) {
            var startInfo = new ProcessStartInfo("journalctl", ["-t", identifier, "-o", "json", "--no-pager"]) {
                RedirectStandardOutput = true
            };
            using var process = Process.Start(startInfo) ??
                                throw new InvalidOperationException("Could not run journalctl.");
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            var entries = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line) ??
                                throw new InvalidOperationException($"journalctl wrote no entry: {line}"))
                .ToArray();
            if (entries.Length >= count || DateTime.Now > deadline)
                return entries;

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
