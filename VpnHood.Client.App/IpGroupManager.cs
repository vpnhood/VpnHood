using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Client.Device;
using VpnHood.Common.Logging;

namespace VpnHood.Client.App
{
    public class IpGroupManager
    {
        private readonly string _ipGroupsFilePath;
        public IpGroup[] IpGroups = Array.Empty<IpGroup>();

        public IpGroupManager(string ipGroupsFilePath)
        {
            _ipGroupsFilePath = ipGroupsFilePath;
            try
            {
                IpGroups = JsonSerializer.Deserialize<IpGroup[]>(File.ReadAllText(ipGroupsFilePath))
                           ?? throw new FormatException($"Could deserialize {ipGroupsFilePath}!");
            }
            catch
            {
                // ignored
            }
        }

        private string IpGroupsFolderPath => Path.Combine(Path.GetDirectoryName(_ipGroupsFilePath)!, "ipgroups");

        public async Task AddFromIp2Location(Stream ipLocationsStream)
        {
            // extract IpGroups
            Dictionary<string, IpGroupNetwork> ipGroupNetworks = new();
            using var streamReader = new StreamReader(ipLocationsStream);
            while (!streamReader.EndOfStream)
            {
                var line = await streamReader.ReadLineAsync();
                var items = line.Replace("\"", "").Split(',');
                if (items.Length != 4)
                    continue;

                var ipGroupId = items[2].ToLower();
                if (ipGroupId == "-") continue;
                if (ipGroupId == "um") ipGroupId = "us";

                if (!ipGroupNetworks.TryGetValue(ipGroupId, out var ipGroupNetwork))
                {
                    var ipGroupName = ipGroupId switch
                    {
                        "us" => "United States",
                        "gb" => "United Kingdom",
                        _ => items[3]
                    };
                    ipGroupName = Regex.Replace(ipGroupName, @"\(.*?\)", "").Replace("  ", " ");

                    ipGroupNetwork = new IpGroupNetwork(ipGroupName, ipGroupId);
                    ipGroupNetworks.Add(ipGroupId, ipGroupNetwork);
                }

                var ipRange = new IpRange(long.Parse(items[0]), long.Parse(items[1]));
                ipGroupNetwork.IpRanges.Add(ipRange);
            }

            //generating files
            VhLogger.Instance.LogTrace($"Generating IpGroups files. IpGroupCount: {ipGroupNetworks.Count}");
            Directory.CreateDirectory(IpGroupsFolderPath);
            foreach (var item in ipGroupNetworks)
            {
                var ipGroup = item.Value;
                var filePath = Path.Combine(IpGroupsFolderPath, $"{ipGroup.IpGroupId}.json");
                await using var fileStream = File.Create(filePath);
                await JsonSerializer.SerializeAsync(fileStream, ipGroup.IpRanges);
            }

            //generating ipGroupData
            VhLogger.Instance.LogTrace($"Generating IpGroups files. IpGroupCount: {ipGroupNetworks.Count}");
            IpGroups = IpGroups.Concat(ipGroupNetworks.Values.Select(x => new IpGroup(x.IpGroupId, x.IpGroupName)))
                .ToArray();

            // save
            await File.WriteAllTextAsync(_ipGroupsFilePath, JsonSerializer.Serialize(IpGroups));
        }

        public IpRange[] GetIpRanges(string ipGroupId)
        {
            var filePath = Path.Combine(IpGroupsFolderPath, $"{ipGroupId}.json");
            return JsonSerializer.Deserialize<IpRange[]>(File.ReadAllText(filePath)) ??
                   throw new Exception($"Could not deserialize {filePath}!");
        }

        private class IpGroupNetwork : IpGroup
        {
            public IpGroupNetwork(string ipGroupName, string ipGroupId)
                : base(ipGroupName, ipGroupId)
            {
            }

            public List<IpRange> IpRanges { get; } = new();
        }
    }
}