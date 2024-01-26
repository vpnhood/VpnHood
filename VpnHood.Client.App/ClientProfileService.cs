using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Common;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App;

public class ClientProfileService
{
    private const string FilenameProfiles = "vpn_profiles.json";
    private readonly string _folderPath;
    private readonly List<ClientProfile> _clientProfiles;

    private string ClientProfilesFilePath => Path.Combine(_folderPath, FilenameProfiles);

    public ClientProfileService(string folderPath)
    {
        _folderPath = folderPath ?? throw new ArgumentNullException(nameof(folderPath));
        ClientProfileServiceLegacy.Migrate(folderPath, ClientProfilesFilePath);
        _clientProfiles = Load().ToList();
    }

    public ClientProfile? FindById(Guid clientProfileId)
    {
        return _clientProfiles.SingleOrDefault(x => x.ClientProfileId == clientProfileId);
    }

    public ClientProfile? FindByTokenId(string tokenId)
    {
        return _clientProfiles.SingleOrDefault(x => x.Token.TokenId == tokenId);
    }

    public ClientProfile Get(Guid clientProfileId)
    {
        return _clientProfiles.Single(x => x.ClientProfileId == clientProfileId);
    }

    public Token GetToken(string tokenId)
    {
        var clientProfile = FindByTokenId(tokenId) ?? throw new NotExistsException($"TokenId does not exist. TokenId: {tokenId}");
        if (clientProfile == null) throw new KeyNotFoundException();
        return clientProfile.Token;
    }

    public ClientProfile[] List()
    {
        return _clientProfiles.ToArray();
    }

    public void Remove(Guid clientProfileId)
    {
        var clientProfile = _clientProfiles.Single(x => x.ClientProfileId == clientProfileId);
        _clientProfiles.Remove(clientProfile);
        Save();
    }

    public void Update(ClientProfile clientProfile)
    {
        var index = _clientProfiles.FindIndex(x => x.ClientProfileId == clientProfile.ClientProfileId);
        if (index == -1)
            throw new NotExistsException($"ClientProfile does not exist. ClientProfileId: {clientProfile.ClientProfileId}");

        _clientProfiles[index] = clientProfile;

        // fix name
        clientProfile.ClientProfileName = clientProfile.ClientProfileName?.Trim();
        if (string.IsNullOrWhiteSpace(clientProfile.ClientProfileName) || clientProfile.ClientProfileName == clientProfile.Token.Name?.Trim())
            clientProfile.ClientProfileName = null;

        Save();
    }


    public ClientProfile ImportAccessKey(string accessKey)
    {
        var token = Token.FromAccessKey(accessKey);
        return ImportAccessToken(token);
    }

    public ClientProfile ImportAccessToken(Token token)
    {
        // update tokens
        foreach (var clientProfile in _clientProfiles.Where(clientProfile => clientProfile.Token.TokenId == token.TokenId))
            clientProfile.Token = token;

        // add if it is a new token
        if (_clientProfiles.All(x => x.Token.TokenId != token.TokenId))
            _clientProfiles.Add(new ClientProfile
            {
                ClientProfileId = Guid.NewGuid(),
                ClientProfileName = token.Name,
                Token = token
            });

        // save profiles
        Save();

        var ret = _clientProfiles.First(x => x.Token.TokenId == token.TokenId);
        return ret;
    }

    public bool UpdateTokenByAccessKey(Token token, string accessKey)
    {
        try
        {
            var newToken = Token.FromAccessKey(accessKey);
            if (VhUtil.JsonEquals(token, newToken))
                return false;

            if (token.TokenId != newToken.TokenId)
                throw new Exception("Could not update the token via access key because its token ID is not the same.");

            ImportAccessToken(newToken);
            return true;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not update token from the given access-key.");
            return false;
        }

    }

    [Obsolete("Temporary and will be removed by March 1 2024")]
    private async Task<bool> UpdateTokenFromUrlV3(Token token)
    {
        // allow update from v3 to v4
        VhLogger.Instance.LogInformation("Trying to get new token from token url v3, Url: {Url}", token.ServerToken.Url);
        try
        {
            using var client = new HttpClient();
            var accessKey = await VhUtil.RunTask(client.GetStringAsync(token.ServerToken.Url), TimeSpan.FromSeconds(20));
            var newToken = Token.FromAccessKey(accessKey);
            if (newToken.TokenId != token.TokenId)
            {
                VhLogger.Instance.LogInformation("Token has not been updated.");
                return false;
            }

            //update store
            token = newToken;
            ImportAccessToken(token);
            VhLogger.Instance.LogInformation("Token has been updated. TokenId: {TokenId}, SupportId: {SupportId}",
                VhLogger.FormatId(token.TokenId), VhLogger.FormatId(token.SupportId));
            return true;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not update token from token url.");
            return false;
        }

    }

    public async Task<bool> UpdateTokenFromUrl(Token token)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        if (token.ServerToken.Secret == null)
            return await UpdateTokenFromUrlV3(token);
#pragma warning restore CS0618 // Type or member is obsolete

        if (string.IsNullOrEmpty(token.ServerToken.Url) || token.ServerToken.Secret == null)
            return false;

        // update token
        VhLogger.Instance.LogInformation("Trying to get a new ServerToken from url. Url: {Url}", VhLogger.FormatHostName(token.ServerToken.Url));
        try
        {
            using var client = new HttpClient();
            var encryptedServerToken = await VhUtil.RunTask(client.GetStringAsync(token.ServerToken.Url), TimeSpan.FromSeconds(20));
            var newServerToken = ServerToken.Decrypt(token.ServerToken.Secret, encryptedServerToken);

            // return older only if token body is same and created time is newer
            if (!token.ServerToken.IsTokenUpdated(newServerToken))
            {
                VhLogger.Instance.LogInformation("The remote ServerToken is not new and has not been updated.");
                return false;
            }

            //update store
            token = VhUtil.JsonClone<Token>(token);
            token.ServerToken = newServerToken;
            ImportAccessToken(token);
            VhLogger.Instance.LogInformation("ServerToken has been updated.");
            return true;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not update ServerToken from url.");
            return false;
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ClientProfilesFilePath)!);
        File.WriteAllText(ClientProfilesFilePath, JsonSerializer.Serialize(_clientProfiles));
    }

    private IEnumerable<ClientProfile> Load()
    {
        try
        {
            var json = File.ReadAllText(ClientProfilesFilePath);
            return VhUtil.JsonDeserialize<ClientProfile[]>(json);
        }
        catch
        {
            return [];
        }
    }

}