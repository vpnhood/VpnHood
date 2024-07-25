using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;
using VpnHood.Server.SystemInformation;

namespace VpnHood.Server.App.Services.Linux;

public class LinuxSystemInfoProvider : ISystemInfoProvider
{
    private static long? GetMemInfoValue(string key)
    {
        try {
            var meminfo = File.ReadAllText("/proc/meminfo");
            var memTotalLine = meminfo.Split('\n').FirstOrDefault(line => line.StartsWith($"{key}:"));
            if (memTotalLine == null)
                return null;

            var tokenize = memTotalLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokenize.Length < 1)
                return null;

            return long.Parse(tokenize[1]) * 1000;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, $"Could not read {key} form /proc/meminfo.");
            return null;
        }
    }

    private long _lastCpuTotalTime;
    private long _lastCpuIdleTime;
    private int _lastUsage;

    private int? GetCpuUsage()
    {
        try {
            // Read the first line of the /proc/stat file
            var statLine = File.ReadAllLines("/proc/stat")[0];

            // Split the line into fields
            var fields = statLine
                .Split(' ')
                .Where(x => x.Trim() != "").ToArray();

            // Parse the fields
            var userTime = long.Parse(fields[1]);
            var niceTime = long.Parse(fields[2]);
            var systemTime = long.Parse(fields[3]);
            var idleTime = long.Parse(fields[4]);
            var ioWaitTime = long.Parse(fields[5]);
            var irqTime = long.Parse(fields[6]);
            var softIrqTime = long.Parse(fields[7]);

            // Calculate the total CPU time
            var totalTime = userTime + niceTime + systemTime + idleTime + ioWaitTime + irqTime + softIrqTime;

            // Calculate the CPU usage
            var lapIdleTime = idleTime - _lastCpuIdleTime;
            var lapTotalTime = totalTime - _lastCpuTotalTime;
            if (lapTotalTime == 0)
                return _lastUsage;

            var usage = 100.0 * (lapTotalTime - lapIdleTime) / lapTotalTime;

            _lastCpuIdleTime = idleTime;
            _lastCpuTotalTime = totalTime;
            _lastUsage = (int)usage;
            return (int)usage;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not read CPU usage form /proc/stat.");
            return null;
        }
    }

    public SystemInfo GetSystemInfo()
    {
        return new SystemInfo {
            OsInfo = GetOperatingSystemInfo(),
            TotalMemory = GetMemInfoValue("MemTotal"),
            AvailableMemory = GetMemInfoValue("MemAvailable"),
            CpuUsage = GetCpuUsage(),
            LogicalCoreCount = Environment.ProcessorCount
        };
    }

    public string GetOperatingSystemInfo()
    {
        string? prettyName = null;
        if (File.Exists("/etc/os-release")) {
            var items = File
                .ReadAllLines("/etc/os-release")
                .Select(line => line.Split('='))
                .Where(split => split.Length >= 1 && !string.IsNullOrEmpty(split[0]))
                .ToDictionary(split => split[0], split => split[1]);

            if (items.TryGetValue("PRETTY_NAME", out prettyName))
                prettyName = prettyName.Replace("\"", "").Trim();
        }

        if (string.IsNullOrEmpty(prettyName)) prettyName = "Linux";
        prettyName += $", {RuntimeInformation.OSArchitecture}";
        return prettyName.Trim();
    }
}