using System.Diagnostics;
using System.Net.Sockets;

namespace VpnHood.Test;

// Diagnostic for sporadic bind failures. Parallel test hosts pre-allocate ports by
// probe-and-release, so another process can grab the port before the real bind; a
// wildcard+exclusive holder surfaces as WSAEACCES (10013) instead of WSAEADDRINUSE.
// Reports who owns the port at failure time so the test log names the culprit.

//todo: remove after diagnostics are no longer needed (or move to a test-only project if we want to keep it around)
public static class PortDiagnostics
{
    public static string GetPortOwners(int port, ProtocolType protocolType)
    {
        try {
            if (!OperatingSystem.IsWindows())
                return "(port owner lookup is only implemented on Windows)";

            var protocol = protocolType == ProtocolType.Udp ? "udp" : "tcp";
            using var process = Process.Start(new ProcessStartInfo {
                FileName = "netstat",
                Arguments = $"-ano -p {protocol}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process == null)
                return "(could not start netstat)";

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            var owners = output
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Contains($":{port} "))
                .Select(line => $"{line} [{GetProcessName(line)}]")
                .ToArray();

            return owners.Length > 0
                ? $"Port {port}/{protocol} is currently owned by: {string.Join(" | ", owners)}"
                : $"No process owns port {port}/{protocol} now; it was likely held transiently.";
        }
        catch (Exception ex) {
            return $"(failed to get port owners: {ex.Message})";
        }
    }

    private static string GetProcessName(string netstatLine)
    {
        try {
            var lastColumn = netstatLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
            using var process = Process.GetProcessById(int.Parse(lastColumn));
            return process.ProcessName;
        }
        catch {
            return "unknown-process";
        }
    }
}
