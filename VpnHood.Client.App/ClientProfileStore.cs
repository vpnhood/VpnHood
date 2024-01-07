using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Common;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App;

public class ClientProfileStore
{
    private const string FilenameProfiles = "profiles.json";
    private const string FilenameTokens = "tokens.json";
    private readonly string _folderPath;
    private Token[] _tokens;

    public ClientProfileStore(string folderPath)
    {
        _folderPath = folderPath ?? throw new ArgumentNullException(nameof(folderPath));
        ClientProfiles = LoadObjectFromFile<ClientProfile[]>(ClientProfilesFileName) ?? Array.Empty<ClientProfile>();
        _tokens = LoadObjectFromFile<Token[]>(TokensFileName) ?? Array.Empty<Token>();
    }

    private string TokensFileName => Path.Combine(_folderPath, FilenameTokens);
    private string ClientProfilesFileName => Path.Combine(_folderPath, FilenameProfiles);

    public ClientProfile[] ClientProfiles { get; private set; }

    public ClientProfileItem[] ClientProfileItems
    {
        get
        {
            var ret = new List<ClientProfileItem>();
            foreach (var clientProfile in ClientProfiles)
                try
                {
                    ret.Add(new ClientProfileItem { ClientProfile = clientProfile, Token = GetToken(clientProfile.TokenId) });
                }
                catch (Exception ex)
                {
                    RemoveClientProfile(clientProfile.ClientProfileId);
                    VhLogger.Instance.LogError($"Could not load token {clientProfile.TokenId}", ex.Message);
                }

            return ret.ToArray();
        }
    }

    public Token GetToken(string tokenId)
    {
        var token = _tokens.FirstOrDefault(x => x.TokenId == tokenId);
        if (token == null) throw new KeyNotFoundException($"TokenId does not exist. TokenId: {tokenId}");
        return token;
    }

    public async Task<Token> UpdateTokenFromUrl(Token token)
    {
        if (string.IsNullOrEmpty(token.ServerToken.Url) || token.ServerToken.Secret == null)
            return token;

        // update token
        VhLogger.Instance.LogInformation("Trying to get new server token from url. Url: {Url}", VhLogger.FormatDns(token.ServerToken.Url));
        try
        {
            using var client = new HttpClient();
            var encryptedServerToken = await VhUtil.RunTask(client.GetStringAsync(token.ServerToken.Url), TimeSpan.FromSeconds(20));
            var newServerToken = ServerToken.Decrypt(token.ServerToken.Secret, encryptedServerToken);
            if (token.ServerToken.CreatedTime >= newServerToken.CreatedTime)
            {
                VhLogger.Instance.LogInformation("ServerToken has not been updated.");
                return token;
            }

            //update store
            token = VhUtil.JsonClone<Token>(token);
            token.ServerToken = newServerToken;
            AddAccessToken(token); 
            VhLogger.Instance.LogInformation("ServerToken has been updated.");
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not update ServerToken from url.");
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
        if (clientProfile.ClientProfileId == Guid.Empty)
            throw new ArgumentNullException(nameof(clientProfile.ClientProfileId),
                $@"{nameof(ClientProfile)} does not have {nameof(clientProfile.ClientProfileId)}");

        if (string.IsNullOrEmpty(clientProfile.TokenId))
            throw new ArgumentNullException(nameof(clientProfile.TokenId), @"ClientProfile does not have tokenId");

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
        Directory.CreateDirectory(Path.GetDirectoryName(TokensFileName)!);
        Directory.CreateDirectory(Path.GetDirectoryName(ClientProfilesFileName)!);

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

    public AccessKeyStatus GetAccessKeyStatus(string accessKey)
    {
        var token = Token.FromAccessKey(accessKey);
        var clientProfile = ClientProfiles.FirstOrDefault(x => x.TokenId == token.TokenId);
        var ret = new AccessKeyStatus
        {
            Name = clientProfile?.Name ?? token.Name,
            SupportId = token.SupportId.ToString(),
            ClientProfile = clientProfile
        };

        return ret;
    }

    public ClientProfile AddAccessKey(string accessKey)
    {
        var token = Token.FromAccessKey(accessKey);
        return AddAccessToken(token);
    }

    public ClientProfile AddAccessToken(Token token)
    {
        // update tokens
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