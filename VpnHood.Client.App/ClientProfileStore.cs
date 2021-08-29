using VpnHood.Logging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VpnHood.Common;
using System.Net.Http;
using System.Threading.Tasks;

namespace VpnHood.Client.App
{

    public class ClientProfileStore
    {
        private const string FILENAME_Profiles = "profiles.json";
        private const string FILENAME_Tokens = "tokens.json";
        private readonly string _folderPath;
        private Token[] _tokens;
        private string TokensFileName => Path.Combine(_folderPath, FILENAME_Tokens);
        private string ClientProfilesFileName => Path.Combine(_folderPath, FILENAME_Profiles);

        public ClientProfile[] ClientProfiles { get; private set; }

        public ClientProfileStore(string folderPath)
        {
            _folderPath = folderPath ?? throw new ArgumentNullException(nameof(folderPath));
            ClientProfiles = LoadObjectFromFile<ClientProfile[]>(ClientProfilesFileName) ?? new ClientProfile[0];
            _tokens = LoadObjectFromFile<Token[]>(TokensFileName) ?? new Token[0];
        }

        public ClientProfileItem[] ClientProfileItems
        {
            get
            {
                var ret = new List<ClientProfileItem>();
                foreach (var clientProfile in ClientProfiles)
                {
                    try
                    {
                        ret.Add(new ClientProfileItem(clientProfile: clientProfile, token: GetToken(clientProfile.TokenId)));
                    }
                    catch (Exception ex)
                    {
                        RemoveClientProfile(clientProfile.ClientProfileId);
                        VhLogger.Instance.LogError($"Could not load token {clientProfile.TokenId}", ex.Message);
                    }
                }
                return ret.ToArray();
            }
        }

        public Token GetToken(Guid tokenId, bool autoUpdate = false) => GetToken(tokenId, false, autoUpdate);
        public ClientProfileItem GetClientProfileItem(Guid clientProfileId) => ClientProfileItems.First(x => x.ClientProfile.ClientProfileId == clientProfileId);

        internal Token GetToken(Guid tokenId, bool withSecret, bool autoUpdate)
        {
            var token = _tokens.Where(x => x.TokenId == tokenId).FirstOrDefault();
            if (token == null) throw new KeyNotFoundException($"{nameof(tokenId)} does not exists. TokenId {tokenId}");

            // clone token
            token = (Token)token.Clone();

            // update token
            if (token.Url != null && autoUpdate)
                token = UpdateTokenFromUrl(token).Result;

            if (!withSecret)
                token.Secret = Array.Empty<byte>();
            return token;
        }

        private async Task<Token> UpdateTokenFromUrl(Token token)
        {
            // update token
            VhLogger.Instance.LogInformation($"Trying to get new token from token url, Url: {token.Url}");
            try
            {
                using var client = new HttpClient();
                var accessKey = await client.GetStringAsync(token.Url);
                AddAccessKey(accessKey); //update store
                token = Token.FromAccessKey(accessKey);
                VhLogger.Instance.LogInformation($"Updated TokenId: {VhLogger.FormatId(token.TokenId)}, SupportId: {VhLogger.FormatId(token.SupportId)}");
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogInformation($"Could not update token from token url, Error: {ex.Message}");
            }
            return token;
        }

        public void RemoveClientProfile(Guid clientProfileId)
        {
            ClientProfiles = ClientProfiles.Where(x => x.ClientProfileId != clientProfileId).ToArray();
            Save();
        }

        public void SetClientProfile(ClientProfile clientProfile)
        {
            // find token
            if (clientProfile.ClientProfileId == Guid.Empty) throw new ArgumentNullException(nameof(clientProfile.ClientProfileId), "ClientProfile does not have ClientProfileId");
            if (clientProfile.TokenId == Guid.Empty) throw new ArgumentNullException(nameof(clientProfile.TokenId), "ClientProfile does not have tokenId");
            var token = GetToken(clientProfile.TokenId); //make sure tokenId is valid

            // fix name
            clientProfile.Name = clientProfile.Name?.Trim();
            if (clientProfile.Name == token.Name?.Trim())
                clientProfile.Name = null;

            //replace old; preserve the order
            var index = -1;
            for (var i = 0; i < ClientProfiles.Length; i++)
                if (ClientProfiles[i].ClientProfileId == clientProfile.ClientProfileId)
                    index = i;

            // replace
            if (index != -1)
                ClientProfiles[index] = clientProfile;
            else // add
                ClientProfiles = ClientProfiles.Concat(new[] { clientProfile }).ToArray();

            Save();
        }

        private void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TokensFileName));
            Directory.CreateDirectory(Path.GetDirectoryName(ClientProfilesFileName));

            // remove not used tokens
            _tokens = _tokens.Where(x => ClientProfiles.Any(y => y.TokenId == x.TokenId)).ToArray();

            // save all
            File.WriteAllText(TokensFileName, JsonSerializer.Serialize(_tokens));
            File.WriteAllText(ClientProfilesFileName, JsonSerializer.Serialize(ClientProfiles));
        }

        private static T? LoadObjectFromFile<T>(string filename)
        {
            try
            {
                if (!File.Exists(filename)) return default;
                return JsonSerializer.Deserialize<T>(File.ReadAllText(filename));
            }
            catch
            {
                return default;
            }

        }

        public ClientProfile AddAccessKey(string accessKey)
        {
            var token = Token.FromAccessKey(accessKey);

            // update tokens
            var oldToken = _tokens.FirstOrDefault(x => x.TokenId == token.TokenId);
            _tokens = _tokens.Where(x => x.TokenId != token.TokenId).Concat(new[] { token }).ToArray();

            // find Server Node if exists
            var clientProfile = ClientProfiles.FirstOrDefault(x => x.TokenId == token.TokenId);
            if (clientProfile == null)
            {
                clientProfile = new ClientProfile
                {
                    ClientProfileId = Guid.NewGuid(),
                    TokenId = token.TokenId
                };
                ClientProfiles = ClientProfiles.Concat(new[] { clientProfile }).ToArray();
            }

            Save();
            return clientProfile;
        }
    }
}
