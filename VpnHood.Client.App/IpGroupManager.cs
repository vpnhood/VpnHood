using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VpnHood.Client.Device;
using VpnHood.Logging;

namespace VpnHood.Client
{
    public class IpGroupManager
    {
        private class IpGroupNetwork : IpGroup
        {
            public List<IpRange> IpRanges { get; set; } = new();
        }

        private readonly string _ipGroupsFilePath;
        private string IpGroupsFolderPath => Path.Combine(Path.GetDirectoryName(_ipGroupsFilePath), "ipgroups");
        public IpGroup[] IpGroups = Array.Empty<IpGroup>();

        public IpGroupManager(string ipGroupsFilePath)
        {
            _ipGroupsFilePath = ipGroupsFilePath;
            try { IpGroups = JsonSerializer.Deserialize<IpGroup[]>(File.ReadAllText(ipGroupsFilePath)); } catch { }
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
                if (ipGroupId == "-")
                    continue;

                if (!ipGroupNetworks.TryGetValue(ipGroupId, out var ipGroupNetwork))
                {
                    ipGroupNetwork = new()
                    {
                        IpGroupName = items[3],
                        IpGroupId = ipGroupId
                    };
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
            IpGroups = IpGroups.Concat(ipGroupNetworks.Values.Select(x => new IpGroup { IpGroupId = x.IpGroupId, IpGroupName = x.IpGroupName })).ToArray();

            // save
            File.WriteAllText(_ipGroupsFilePath, JsonSerializer.Serialize(IpGroups));
        }

        public IpRange[] GetIpRanges(string ipGroupId)
        {
            var filePath = Path.Combine(IpGroupsFolderPath, $"{ipGroupId}.json");
            return JsonSerializer.Deserialize<IpRange[]>(File.ReadAllText(filePath));
        }
    }
}
