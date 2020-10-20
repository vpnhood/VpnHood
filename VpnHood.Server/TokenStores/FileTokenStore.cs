using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Tasks;

namespace VpnHood.Server.TokenStores
{
    public class FileTokenStore : ITokenStore
    {
        private readonly string _folderPath;
        private const string FILEEXT_token = ".token";
        private const string FILEEXT_tokenUsage = ".tokenUsage";
        private const string FILENAME_SupportIdIndex = "supportId.index";
        private readonly Dictionary<int, Guid> _supportIdIndex;
        private string FILEPATH_SupportIdIndex => Path.Combine(_folderPath, FILENAME_SupportIdIndex);
        private string GetTokenFileName(Guid tokenId) => Path.Combine(_folderPath, tokenId.ToString() + FILEEXT_token);
        private string GetTokenUsageFileName(Guid tokenId) => Path.Combine(_folderPath, tokenId.ToString() + FILEEXT_tokenUsage);

        public FileTokenStore(string filePath)
        {
            _folderPath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            Directory.CreateDirectory(_folderPath);
            _supportIdIndex = LoadSupportIdIndex(FILEPATH_SupportIdIndex);
        }

        /// <summary>
        /// Add token to this store
        /// </summary>
        /// <param name="tokenInfo">Initialize token info; Token.TokenId must be zero</param>
        /// <returns>new tokenId</returns>
        public int AddToken(TokenInfo tokenInfo)
        {
            if (tokenInfo is null) throw new ArgumentNullException(nameof(tokenInfo));
            if (tokenInfo.Token is null) new ArgumentNullException(nameof(tokenInfo.Token));
            if (tokenInfo.Token.SupportId != 0) new ArgumentException($"{nameof(tokenInfo.Token.SupportId)} is not zero!");
            if (tokenInfo.Token.TokenId == Guid.Empty) tokenInfo.Token.TokenId = Guid.NewGuid();
            var token = tokenInfo.Token;

            // assign new tokeId
            tokenInfo.Token.SupportId = GetNewTokenSupportId();

            // write tokenUsage
            WriteTokenUsage(token.TokenId, tokenInfo.TokenUsage);

            // Write token
            File.WriteAllText(GetTokenFileName(token.TokenId), JsonSerializer.Serialize(token));

            // update index
            _supportIdIndex.Add(token.SupportId, token.TokenId);
            WriteSupportIdIndex();

            return token.SupportId;
        }

        public async Task RemoveToken(Guid tokenId)
        {
            // remove index
            var tokenInfo = await GetTokenInfo(tokenId);
            _supportIdIndex.Remove(tokenInfo.Token.SupportId);

            // delete files
            File.Delete(GetTokenUsageFileName(tokenId));
            File.Delete(GetTokenFileName(tokenId));

            // remove index
            WriteSupportIdIndex();
        }

        private Dictionary<int, Guid> LoadSupportIdIndex(string fileName)
        {
            var ret =  new Dictionary<int, Guid>();
            try
            {
                if (File.Exists(fileName))
                {
                    var json = File.ReadAllText(fileName);
                    var collection = JsonSerializer.Deserialize<KeyValuePair<int, Guid>[]>(json);
                    ret = new Dictionary<int, Guid>(collection);
                }
            }
            catch{}

            return ret;
        }

        private void WriteSupportIdIndex()
        {
            var arr = _supportIdIndex.ToArray();
            File.WriteAllText(FILEPATH_SupportIdIndex, JsonSerializer.Serialize(arr));
        }

        private void WriteTokenUsage(Guid tokenId, TokenUsage tokenUsage)
        {
            // write token info
            var json = JsonSerializer.Serialize(tokenUsage);
            File.WriteAllText(GetTokenUsageFileName(tokenId), json);
        }

        public Guid[] GetAllTokenIds() => _supportIdIndex.Select(x => x.Value).ToArray();
        private int GetNewTokenSupportId()
        {
            return _supportIdIndex.Count == 0 ? 1 : _supportIdIndex.Max(x => x.Key) + 1;
        }

        public Guid TokenIdFromSupportId(int supportId) => _supportIdIndex[supportId];

        /// <returns>null if token does not exist</returns>
        public async Task<TokenUsage> GetTokenUsage(Guid tokenId)
        {
            var path = GetTokenUsageFileName(tokenId);
            if (!File.Exists(path))
                return null;

            // read tokenUsage
            var json = await File.ReadAllTextAsync(path);
            var tokenUsage = JsonSerializer.Deserialize<TokenUsage>(json);

            return tokenUsage;
        }

        public async Task<TokenInfo> GetTokenInfo(Guid tokenId)
        {
            var path = GetTokenFileName(tokenId);
            if (!File.Exists(path))
                return null;
            
            var json = await File.ReadAllTextAsync(path);
            var ret = new TokenInfo()
            {
                TokenUsage = await GetTokenUsage(tokenId),
                Token = JsonSerializer.Deserialize<Token>(json)
            };

            return ret;
        }

        public async Task<TokenUsage> AddTokenUsage(Guid tokenId, long sentByteCount, long receivedByteCount)
        {
            var tokenUsage = await GetTokenUsage(tokenId);
            tokenUsage.SentByteCount += sentByteCount;
            tokenUsage.ReceivedByteCount += receivedByteCount;
            WriteTokenUsage(tokenId, tokenUsage);
            return tokenUsage;
        }
    }
}
