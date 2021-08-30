using System;
using System.IO;
using System.Text.RegularExpressions;

namespace VpnHood.Server.SystemInformation
{
    public class LinuxSystemInfoProvider : ISystemInfoProvider
    {
        private class MemInfoMatch
        {
            public Regex Regex { get; }
            public Action<string> UpdateValue { get; }

            public MemInfoMatch(string pattern, Action<string> update)
            {
                Regex = new Regex(pattern, RegexOptions.Compiled);
                UpdateValue = update;
            }
        }

        public SystemInfo GetSystemInfo()
        {
            long totalMemory = 0;
            long freeMemory = 0;
            long buffers = 0;
            long cached = 0;

            string[] memInfoLines = File.ReadAllLines(@"/proc/meminfo");

            MemInfoMatch[] memInfoMatches =
            {
                new MemInfoMatch(@"^Buffers:\s+(\d+)", value => buffers = Convert.ToInt64(value)),
                new MemInfoMatch(@"^Cached:\s+(\d+)", value => cached = Convert.ToInt64(value)),
                new MemInfoMatch(@"^MemFree:\s+(\d+)", value => freeMemory = Convert.ToInt64(value)),
                new MemInfoMatch(@"^MemTotal:\s+(\d+)", value => totalMemory = Convert.ToInt64(value))
            };

            foreach (var memInfoLine in memInfoLines)
            {
                foreach (var memInfoMatch in memInfoMatches)
                {
                    Match match = memInfoMatch.Regex.Match(memInfoLine);
                    if (match.Groups[1].Success)
                    {
                        var value = match.Groups[1].Value;
                        memInfoMatch.UpdateValue(value);
                    }
                }
            }

            return new SystemInfo(
                totalMemory * 1000, 
                (freeMemory + cached + buffers) * 1000);
        }

        public string GetOperatingSystemInfo()
        {
            var ret = Environment.OSVersion.ToString() + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");

            // find linux distribution
            if (File.Exists("/proc/version"))
                ret += ", " + File.ReadAllText("/proc/version");
            else if (File.Exists("/etc/lsb-release"))
                ret += ", " + File.ReadAllText("/etc/lsb-release");

            return ret.Trim();
        }
    }
}
