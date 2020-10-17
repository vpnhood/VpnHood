using VpnHood.Loggers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace VpnHood.Client.App
{

    public class AppClientProfileStore
    {
        private const string FILENAME_Profiles = "profiles.json";
        private const string FILENAME_Tokens = "tokens.json";
        private readonly string _folderPath;
        private Token[] _tokens;
        private string TokensFileName => Path.Combine(_folderPath, FILENAME_Tokens);
        private string ClientProfilesFileName => Path.Combine(_folderPath, FILENAME_Profiles);

        public AppClientProfile[] ClientProfiles { get; private set; }

        public AppClientProfileStore(string folderPath)
        {
            _folderPath = folderPath ?? throw new ArgumentNullException(nameof(folderPath));
            ClientProfiles = LoadObjectFromFile<AppClientProfile[]>(ClientProfilesFileName) ?? new AppClientProfile[0];
            _tokens = LoadObjectFromFile<Token[]>(TokensFileName) ?? new Token[0];
        }

        public AppClientProfileItem[] ClientProfileItems
        {
            get
            {
                var ret = new List<AppClientProfileItem>();
                foreach (var clientProfile in ClientProfiles)
                {
                    try
                    {
                        ret.Add(new AppClientProfileItem()
                        {
                            ClientProfile = clientProfile,
                            Token = GetToken(clientProfile.TokenId)
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Current.LogError($"Could not load token {clientProfile.TokenId}", ex.Message);
                    }
                }
                return ret.ToArray();
            }
        }

        public Token GetToken(Guid tokenId) => GetToken(tokenId, false);
        public AppClientProfileItem GetClientProfileItem(Guid clientProfileId) => ClientProfileItems.First(x => x.ClientProfile.ClientProfileId == clientProfileId);

        internal Token GetToken(Guid tokenId, bool withSecret)
        {
            var token = _tokens.Where(x => x.TokenId == tokenId).FirstOrDefault();
            if (token == null) throw new KeyNotFoundException($"nameof(tokenId) does not exists");

            // close token
            token = (Token)token.Clone();
            if (!withSecret)
                token.Secret = null;
            return token;
        }

        public void RemoveClientProfile(Guid clientProfileId)
        {
            ClientProfiles = ClientProfiles.Where(x => x.ClientProfileId != clientProfileId).ToArray();
            Save();
        }

        public void SetClientProfile(AppClientProfile clientProfile)
        {
            // find token
            if (clientProfile.ClientProfileId == Guid.Empty) throw new ArgumentNullException(nameof(clientProfile.ClientProfileId), "ClientProfile does not have ClientProfileId");
            if (clientProfile.TokenId == Guid.Empty) throw new ArgumentNullException(nameof(clientProfile.TokenId), "ClientProfile does not have tokenId");
            var token = GetToken(clientProfile.TokenId);

            //remove old one and add new node
            ClientProfiles = ClientProfiles.Where(x => x.ClientProfileId != clientProfile.ClientProfileId).Concat(new[] { clientProfile }).ToArray();
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

        private static T LoadObjectFromFile<T>(string filename)
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

        public AppClientProfile AddAccessKey(string accessKey)
        {
            var token = Token.FromAccessKey(accessKey);

            // update tokens
            var oldToken = _tokens.FirstOrDefault(x => x.TokenId == token.TokenId);
            _tokens = _tokens.Where(x => x.TokenId != token.TokenId).Concat(new[] { token }).ToArray();

            // find Server Node if exists
            var clientProfile = ClientProfiles.FirstOrDefault(x => x.TokenId == token.TokenId);
            if (clientProfile == null)
            {
                clientProfile = new AppClientProfile()
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
