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
        private readonly string _folderPath;
        private const string FILEEXT_token = ".token";
        private const string FILEEXT_clientUsage = ".clientUsage";
        private const string FILENAME_SupportIdIndex = "supportId.index";
        private readonly Dictionary<int, Guid> _supportIdIndex;
        private string FILEPATH_SupportIdIndex => Path.Combine(_folderPath, FILENAME_SupportIdIndex);
        private string GetTokenFileName(Guid tokenId) => Path.Combine(_folderPath, tokenId.ToString() + FILEEXT_token);
        private string GetClientUsageFileName(Guid tokenId) => Path.Combine(_folderPath, tokenId.ToString() + FILEEXT_clientUsage);

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
        /// <param name="clientInfo">Initialize token info; Token.SupportId must be zero</param>
        /// <returns>tokenSupportId</returns>
        public int AddToken(ClientInfo clientInfo)
        {
            if (clientInfo is null) throw new ArgumentNullException(nameof(clientInfo));
            if (clientInfo.Token is null) new ArgumentNullException(nameof(clientInfo.Token));
            if (clientInfo.Token.SupportId != 0) new ArgumentException($"{nameof(clientInfo.Token.SupportId)} is not zero!");
            if (clientInfo.Token.TokenId == Guid.Empty) clientInfo.Token.TokenId = Guid.NewGuid();
            var token = clientInfo.Token;

            // add defautl values
            if (clientInfo.ClientUsage == null) clientInfo.ClientUsage = new ClientUsage();
            if (clientInfo.TokenSettings == null) clientInfo.TokenSettings = new TokenSettings();

            // assign new supportId
            clientInfo.Token.SupportId = GetNewTokenSupportId();

            // write clientUsage
            WriteClientUsage(token.TokenId, clientInfo.ClientUsage);

            // Write clientInfo
            var newClientInfo = JsonSerializer.Deserialize<ClientInfo>(JsonSerializer.Serialize(clientInfo)); // clone
            newClientInfo.ClientUsage = null; // we don't save clientUsage in this file
            File.WriteAllText(GetTokenFileName(token.TokenId), JsonSerializer.Serialize(newClientInfo));

            // update index
            _supportIdIndex.Add(token.SupportId, token.TokenId);
            WriteSupportIdIndex();

            return token.SupportId;
        }

        public async Task RemoveToken(Guid tokenId)
        {
            // remove index
            var clientInfo = await GetClientInfo(ClientIdentityFromTokenId(tokenId), withToken: true);
            _supportIdIndex.Remove(clientInfo.Token.SupportId);

            // delete files
            File.Delete(GetClientUsageFileName(tokenId));
            File.Delete(GetTokenFileName(tokenId));

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

        private void WriteClientUsage(Guid tokenId, ClientUsage clientUsage)
        {
            // write token info
            var json = JsonSerializer.Serialize(clientUsage);
            File.WriteAllText(GetClientUsageFileName(tokenId), json);
        }

        public Guid[] GetAllTokenIds() => _supportIdIndex.Select(x => x.Value).ToArray();
        private int GetNewTokenSupportId()
        {
            return _supportIdIndex.Count == 0 ? 1 : _supportIdIndex.Max(x => x.Key) + 1;
        }

        /// <returns>null if token does not exist</returns>
        private async Task<ClientUsage> GetClientUsage(Guid tokenId)
        {
            var path = GetClientUsageFileName(tokenId);
            if (!File.Exists(path))
                return null;

            // read ClientUsage
            var json = await File.ReadAllTextAsync(path);
            var clientUsage = JsonSerializer.Deserialize<ClientUsage>(json);

            return clientUsage;
        }

        private async Task<ClientInfo> GetClientInfo(ClientIdentity clientIdentity, bool withToken = false, bool withTokenSettings = false, bool withClientUsage = false)
        {
            var tokenId = clientIdentity.TokenId;
            var path = GetTokenFileName(tokenId);
            if (!File.Exists(path))
                return null;

            var json = await File.ReadAllTextAsync(path);
            var ret = JsonSerializer.Deserialize<ClientInfo>(json);

            ret.ClientUsage = withClientUsage ? await GetClientUsage(tokenId) : null;
            ret.Token = withToken ? ret.Token : null;
            ret.TokenSettings = withTokenSettings ? ret.TokenSettings : null;
            if (!withToken) ret.Token = null;
            return ret;
        }

        public Task<ClientInfo> GetClientInfo(Guid tokenId, bool withToken) =>
            GetClientInfo(ClientIdentityFromTokenId(tokenId), withToken);

        public Task<ClientInfo> GetClientInfo(ClientIdentity clientIdentity, bool withToken)
        {
            return GetClientInfo(clientIdentity,
                withClientUsage: true,
                withTokenSettings: true,
                withToken: withToken
                );
        }

        public async Task<ClientInfo> AddClientUsage(ClientIdentity clientIdentity, ClientUsage clientUsage, bool withToken)
        {
            var clientInfo = await GetClientInfo(clientIdentity, withToken);
            if (clientInfo == null)
                return null;

            if (clientUsage != null)
            {
                var newClientUsage = clientInfo.ClientUsage;
                newClientUsage.SentByteCount += clientUsage.SentByteCount;
                newClientUsage.ReceivedByteCount += clientUsage.ReceivedByteCount;
                WriteClientUsage(clientIdentity.TokenId, newClientUsage);
            }
            return clientInfo;
        }
    }
}
