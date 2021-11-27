using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using VpnHood.Server.SystemInformation;

namespace VpnHood.Server.App.SystemInformation
{
    public class LinuxSystemInfoProvider : ISystemInfoProvider
    {
        public SystemInfo GetSystemInfo()
        {
            long totalMemory = 0;
            long freeMemory = 0;
            long buffers = 0;
            long cached = 0;

            string[] memInfoLines = File.ReadAllLines(@"/proc/meminfo");

            MemInfoMatch[] memInfoMatches =
            {
                new(@"^Buffers:\s+(\d+)", value => buffers = Convert.ToInt64(value)),
                new(@"^Cached:\s+(\d+)", value => cached = Convert.ToInt64(value)),
                new(@"^MemFree:\s+(\d+)", value => freeMemory = Convert.ToInt64(value)),
                new(@"^MemTotal:\s+(\d+)", value => totalMemory = Convert.ToInt64(value))
            };

            foreach (var memInfoLine in memInfoLines)
                foreach (var memInfoMatch in memInfoMatches)
                {
                    Match match = memInfoMatch.Regex.Match(memInfoLine);
                    if (match.Groups[1].Success)
                    {
                        var value = match.Groups[1].Value;
                        memInfoMatch.UpdateValue(value);
                    }
                }

            return new SystemInfo(
                totalMemory * 1000,
                (freeMemory + cached + buffers) * 1000);
        }

        public string GetOperatingSystemInfo()
        {

            var items = File
                .ReadAllLines("/etc/os-release")
                .Select(line => line.Split('='))
                .ToDictionary(split => split[0], split => split[1]);

            var ret = items["PRETTY_NAME"]?.Replace("\"", "") ?? RuntimeInformation.OSDescription;
            ret += $", {RuntimeInformation.OSArchitecture}";

            return ret.Trim();
        }

        private class MemInfoMatch
        {
            public MemInfoMatch(string pattern, Action<string> update)
            {
                Regex = new Regex(pattern, RegexOptions.Compiled);
                UpdateValue = update;
            }

            public Regex Regex { get; }
            public Action<string> UpdateValue { get; }
        }
    }
}