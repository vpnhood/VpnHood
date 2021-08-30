using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VpnHood.Client.Device;
using VpnHood.Logging;

namespace VpnHood.Client
{
    public class IpGroupManager
    {
        private class IpGroupNetwork : IpGroup
        {
            public IpGroupNetwork(string ipGroupName, string ipGroupId)
                : base(ipGroupName, ipGroupId)
            {
            }

            public List<IpRange> IpRanges { get; } = new();
        }

        private readonly string _ipGroupsFilePath;
        private string IpGroupsFolderPath => Path.Combine(Path.GetDirectoryName(_ipGroupsFilePath), "ipgroups");
        public IpGroup[] IpGroups = Array.Empty<IpGroup>();

        public IpGroupManager(string ipGroupsFilePath)
        {
            _ipGroupsFilePath = ipGroupsFilePath;
            try
            {
                IpGroups = JsonSerializer.Deserialize<IpGroup[]>(File.ReadAllText(ipGroupsFilePath))
                    ?? throw new FormatException($"Could deserialize {ipGroupsFilePath}!");
            }
            catch { }
        }

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
                    var IpGroupName = items[3];
                    if (ipGroupId == "us") IpGroupName = "United States";
                    if (ipGroupId == "gb") IpGroupName = "United Kingdom";
                    IpGroupName = Regex.Replace(IpGroupName, @"\(.*?\)", "").Replace("  ", " ");

                    ipGroupNetwork = new(IpGroupName, ipGroupId);
                    ipGroupNetworks.Add(ipGroupId, ipGroupNetwork);
                };
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
                using var fileStream = File.Create(filePath);
                await JsonSerializer.SerializeAsync(fileStream, ipGroup.IpRanges);
            }

            //generating ipGroupData
            VhLogger.Instance.LogTrace($"Generating IpGroups files. IpGroupCount: {ipGroupNetworks.Count}");
            IpGroups = IpGroups.Concat(ipGroupNetworks.Values.Select(x => new IpGroup(x.IpGroupId, x.IpGroupName))).ToArray();

            // save
            File.WriteAllText(_ipGroupsFilePath, JsonSerializer.Serialize(IpGroups));
        }

        public IpRange[] GetIpRanges(string ipGroupId)
        {
            var filePath = Path.Combine(IpGroupsFolderPath, $"{ipGroupId}.json");
            return JsonSerializer.Deserialize<IpRange[]>(File.ReadAllText(filePath)) ?? throw new Exception($"Could not deserialize {filePath}!");
        }
    }
}
