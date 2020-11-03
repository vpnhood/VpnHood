using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace VpnHood.Server.AccessServers
{
    public class FileAccessServer : IAccessServer
    {
        public class AccessItem
        {
            public DateTime? ExpirationTime { get; set; }
            public int MaxClient { get; set; }
            public long MaxTraffic { get; set; }
            public Token Token { get; set; }
        }

        private class Usage
        {
            public long SentTraffic { get; set; }
            public long ReceivedTraffic { get; set; }
        }

        private readonly string _folderPath;
        private const string FILEEXT_token = ".token";
        private const string FILEEXT_usage = ".usage";
        private const string FILENAME_SupportIdIndex = "supportId.index";
        private readonly Dictionary<int, Guid> _supportIdIndex;
        private string FILEPATH_SupportIdIndex => Path.Combine(_folderPath, FILENAME_SupportIdIndex);
        private string GetAccessItemFileName(Guid tokenId) => Path.Combine(_folderPath, tokenId.ToString() + FILEEXT_token);
        private string GetUsageFileName(Guid tokenId) => Path.Combine(_folderPath, tokenId.ToString() + FILEEXT_usage);

        public Guid TokenIdFromSupportId(int supportId) => _supportIdIndex[supportId];
        public ClientIdentity ClientIdentityFromSupportId(int supportId) => ClientIdentityFromTokenId(TokenIdFromSupportId(supportId));
        public ClientIdentity ClientIdentityFromTokenId(Guid tokenId) => new ClientIdentity() { TokenId = tokenId };


        public FileAccessServer(string filePath)
        {
            _folderPath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            Directory.CreateDirectory(_folderPath);
            _supportIdIndex = LoadSupportIdIndex(FILEPATH_SupportIdIndex);
        }

        /// <summary>
        /// Add token to this store
        /// </summary>
        /// <param name="accessItem">Initialize token info; Token.SupportId must be zero</param>
        /// <returns>tokenSupportId</returns>
        public int AddAccessItem(AccessItem accessItem)
        {
            if (accessItem is null) new ArgumentNullException(nameof(accessItem));

            var token = accessItem.Token;
            if (token is null) new ArgumentNullException(nameof(accessItem.Token));
            if (token.SupportId != 0) new ArgumentException($"{nameof(token.SupportId)} is not zero!");
            if (token.TokenId == Guid.Empty) token.TokenId = Guid.NewGuid();

            // assign new supportId
            token.SupportId = GetNewTokenSupportId();

            // write usage
            Usage_Write(token.TokenId, new Usage());

            // Write accessItem
            File.WriteAllText(GetAccessItemFileName(token.TokenId), JsonSerializer.Serialize(accessItem));

            // update index
            _supportIdIndex.Add(token.SupportId, token.TokenId);
            WriteSupportIdIndex();

            return token.SupportId;
        }

        public async Task RemoveToken(Guid tokenId)
        {
            // remove index
            var accessItem = await AccessItem_Read(tokenId);
            _supportIdIndex.Remove(accessItem.Token.SupportId);

            // delete files
            File.Delete(GetUsageFileName(tokenId));
            File.Delete(GetAccessItemFileName(tokenId));

            // remove index
            WriteSupportIdIndex();
        }

        private Dictionary<int, Guid> LoadSupportIdIndex(string fileName)
        {
            var ret = new Dictionary<int, Guid>();
            try
            {
                if (File.Exists(fileName))
                {
                    var json = File.ReadAllText(fileName);
                    var collection = JsonSerializer.Deserialize<KeyValuePair<int, Guid>[]>(json);
                    ret = new Dictionary<int, Guid>(collection);
                }
            }
            catch { }

            return ret;
        }

        private void WriteSupportIdIndex()
        {
            var arr = _supportIdIndex.ToArray();
            File.WriteAllText(FILEPATH_SupportIdIndex, JsonSerializer.Serialize(arr));
        }

        private Task Usage_Write(Guid tokenId, Usage usage)
        {
            // write token info
            var json = JsonSerializer.Serialize(usage);
            return File.WriteAllTextAsync(GetUsageFileName(tokenId), json);
        }

        public Guid[] GetAllTokenIds() => _supportIdIndex.Select(x => x.Value).ToArray();
        private int GetNewTokenSupportId()
        {
            return _supportIdIndex.Count == 0 ? 1 : _supportIdIndex.Max(x => x.Key) + 1;
        }

        public async Task<AccessItem> AccessItem_Read(Guid tokenId)
        {
            // read access item
            var accessItemPath = GetAccessItemFileName(tokenId);
            if (!File.Exists(accessItemPath))
                return null;
            var json = await File.ReadAllTextAsync(accessItemPath);
            return JsonSerializer.Deserialize<AccessItem>(json);
        }

        private async Task<Usage> Usage_Read(Guid tokenId)
        {
            // read usageItem
            var usage = new Usage();
            var usagePath = GetUsageFileName(tokenId);
            if (File.Exists(usagePath))
            {
                var json = await File.ReadAllTextAsync(usagePath);
                usage = JsonSerializer.Deserialize<Usage>(json);
            }
            return usage;
        }

        public async Task<Access> GetAccess(ClientIdentity clientIdentity)
        {
            if (clientIdentity is null) throw new ArgumentNullException(nameof(clientIdentity));
            var usage = await Usage_Read(clientIdentity.TokenId);
            return await GetAccess(clientIdentity, usage);
        }

        private async Task<Access> GetAccess(ClientIdentity clientIdentity, Usage usage)
        {
            if (clientIdentity is null) throw new ArgumentNullException(nameof(clientIdentity));
            if (usage is null) throw new ArgumentNullException(nameof(usage));

            var tokenId = clientIdentity.TokenId;
            var accessItem = await AccessItem_Read(tokenId);
            if (accessItem == null)
                return null;

            var access = new Access()
            {
                AccessId = clientIdentity.TokenId.ToString(),
                DnsName = accessItem.Token.DnsName,
                ExpirationTime = accessItem.ExpirationTime,
                MaxClientCount = accessItem.MaxClient,
                MaxTrafficByteCount = accessItem.MaxTraffic,
                Secret = accessItem.Token.Secret,
                ReceivedTrafficByteCount = usage.ReceivedTraffic,
                ServerEndPoint = accessItem.Token.ServerEndPoint,
                SentTrafficByteCount = usage.SentTraffic,
                StatusCode = AccessStatusCode.Ok,
            };

            if (accessItem.MaxTraffic != 0 && usage.SentTraffic + usage.ReceivedTraffic > accessItem.MaxTraffic)
            {
                access.Message = "All traffic has been consumed!";
                access.StatusCode = AccessStatusCode.TrafficOverflow;
            }


            if (accessItem.ExpirationTime != null && accessItem.ExpirationTime < DateTime.Now)
            {
                access.Message = "Access Expired!";
                access.StatusCode = AccessStatusCode.Expired;
            }

            return access;
        }

        public async Task<Access> AddUsage(ClientIdentity clientIdentity, long sentTrafficByteCount, long receivedTrafficByteCount)
        {
            if (clientIdentity is null) throw new ArgumentNullException(nameof(clientIdentity));

            // write usage
            var tokenId = clientIdentity.TokenId;
            var usage = await Usage_Read(tokenId);
            usage.SentTraffic += sentTrafficByteCount;
            usage.ReceivedTraffic += receivedTrafficByteCount;
            await Usage_Write(tokenId, usage);

            return await GetAccess(clientIdentity, usage);
        }

    }
}
