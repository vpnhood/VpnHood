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
            public List<IpNetwork> IpNetworksList { get; set; } = new(); 
        }

        private class IpGroupsData
        {
            public IpGroup[] IpGroups { get; set; }
            public long LastStreamSize { get; set; }
        }

        private readonly string _workingPath;
        private readonly Stream _ipLocationsStream;
        private IpGroupsData _ipGroupsData;

        private IpGroupManager(string workingPath, Stream ipLocationsStream)
        {
            _workingPath = workingPath ?? throw new ArgumentNullException(nameof(workingPath));
            _ipLocationsStream = ipLocationsStream ?? throw new ArgumentNullException(nameof(ipLocationsStream));
        }

        public static async Task<IpGroupManager> Create(string workingPath, Stream ipLocationsStream)
        {
            var ret = new IpGroupManager(workingPath, ipLocationsStream);
            await ret.Load();
            return ret;
        }

        private async Task Load()
        {

            // load _ipGroupsData
            VhLogger.Instance.LogTrace("Loading IpGroups...");
            var ipGroupsFilePath = Path.Combine(_workingPath, "ipgroups.json");
            if (File.Exists(ipGroupsFilePath))
            {
                try
                {
                    using var ipGroupsDataStream = File.OpenRead(ipGroupsFilePath);
                    _ipGroupsData = await JsonSerializer.DeserializeAsync<IpGroupsData>(ipGroupsDataStream);
                    if (_ipGroupsData.LastStreamSize == _ipLocationsStream.Length)
                        return;
                }
                catch { };
            }

            // extract IpGroups
            Dictionary<string, IpGroupNetwork> ipGroupNetworks = new();
            VhLogger.Instance.LogTrace($"Extracting IpGroups. LastDataSize: {_ipGroupsData?.LastStreamSize ?? 0}, CurrentDataSize: {_ipLocationsStream.Length}");
            using var streamReader = new StreamReader(_ipLocationsStream);
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
                var ipRange = IpNetwork.FromIpRange(long.Parse(items[0]), long.Parse(items[1]));
                ipGroupNetwork.IpNetworksList.AddRange(ipRange);
            }

            //generating files
            VhLogger.Instance.LogTrace($"Generating IpGroups files. IpGroupCount: {ipGroupNetworks.Count}");
            try { Directory.Delete(_workingPath, true); } catch { }
            Directory.CreateDirectory(_workingPath);
            foreach (var item in ipGroupNetworks)
            {
                var filePath = Path.Combine(_workingPath, $"{item.Key}.json");
                using var fileStream = File.OpenWrite(filePath);
                await JsonSerializer.SerializeAsync(fileStream, item.Value);
            }

            //generating ipGroupData
            VhLogger.Instance.LogTrace($"Generating IpGroups files. IpGroupCount: {ipGroupNetworks.Count}");
            _ipGroupsData = new IpGroupsData
            {
                LastStreamSize = _ipLocationsStream.Length,
                IpGroups = ipGroupNetworks.Values.Select(x => new IpGroup { IpGroupId = x.IpGroupId, IpGroupName = x.IpGroupName }).ToArray()
            };

            // write ipGroupsData
            using var ipGroupsDataStream2 = File.OpenWrite(ipGroupsFilePath);
            await JsonSerializer.SerializeAsync(ipGroupsDataStream2, _ipGroupsData);
        }

        public IpGroup[] IpGroups => _ipGroupsData.IpGroups;

        public IpNetwork[] GetIpNetworks(string groupName)
        {
            throw new NotImplementedException();
        }

        public IpNetwork[] GetIpNetworks(string[] groupName)
        {
            throw new NotImplementedException();
        }
    }

}
