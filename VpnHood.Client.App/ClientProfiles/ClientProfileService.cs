using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Common;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App.ClientProfiles;

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
        return clientProfile.Token;
    }

    public ClientProfile[] List()
    {
        return _clientProfiles.ToArray();
    }

    public void Remove(Guid clientProfileId)
    {
        var clientProfile =
            _clientProfiles.SingleOrDefault(x => x.ClientProfileId == clientProfileId)
            ?? throw new NotExistsException();

        // BuiltInToken should not be removed
        if (clientProfile.IsBuiltIn)
            throw new UnauthorizedAccessException("Could not overwrite BuiltIn tokens.");

        _clientProfiles.Remove(clientProfile);
        Save();
    }

    public void TryRemoveByTokenId(string tokenId)
    {
        var clientProfiles = _clientProfiles.Where(x => x.Token.TokenId == tokenId).ToArray();
        foreach (var clientProfile in clientProfiles)
            _clientProfiles.Remove(clientProfile);

        Save();
    }

    public ClientProfile Update(Guid clientProfileId, ClientProfileUpdateParams updateParams)
    {
        var clientProfile = _clientProfiles.SingleOrDefault(x => x.ClientProfileId == clientProfileId)
            ?? throw new NotExistsException("ClientProfile does not exists. ClientProfileId: {clientProfileId}");

        // update name
        if (updateParams.ClientProfileName != null)
        {
            var name = updateParams.ClientProfileName.Value?.Trim();
            if (name == clientProfile.Token.Name?.Trim()) name = null; // set default if the name is same as token name
            if (name?.Length == 0) name = null;
            clientProfile.ClientProfileName = name;
        }

        if (updateParams.IsFavorite != null)
            clientProfile.IsFavorite = updateParams.IsFavorite.Value;

        Save();
        return clientProfile;
    }

    public ClientProfile ImportAccessKey(string accessKey)
    {
        return ImportAccessKey(accessKey, false);
    }

    private ClientProfile ImportAccessKey(string accessKey, bool isForAccount)
    {
        var token = Token.FromAccessKey(accessKey);
        return ImportAccessToken(token, overwriteNewer: true, allowOverwriteBuiltIn: false, isForAccount: isForAccount);
    }

    // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
    private ClientProfile ImportAccessToken(Token token, bool overwriteNewer, bool allowOverwriteBuiltIn,
        bool isForAccount = false, bool isBuiltIn = false)
    {
        // make sure no one overwrites built-in tokens
        if (!allowOverwriteBuiltIn && _clientProfiles.Any(x => x.IsBuiltIn && x.Token.TokenId == token.TokenId))
            throw new UnauthorizedAccessException("Could not overwrite BuiltIn tokens.");

        // update tokens
        foreach (var clientProfile in _clientProfiles.Where(clientProfile =>
                     clientProfile.Token.TokenId == token.TokenId))
        {
            if (overwriteNewer || token.IssuedAt >= clientProfile.Token.IssuedAt)
                clientProfile.Token = token;
        }

        // add if it is a new token
        if (_clientProfiles.All(x => x.Token.TokenId != token.TokenId))
            _clientProfiles.Add(new ClientProfile
            {
                ClientProfileId = Guid.NewGuid(),
                ClientProfileName = token.Name,
                Token = token,
                IsForAccount = isForAccount,
                IsBuiltIn = isBuiltIn
            });

        // save profiles
        Save();

        var ret = _clientProfiles.First(x => x.Token.TokenId == token.TokenId);
        return ret;
    }

    internal ClientProfile[] ImportBuiltInAccessKeys(string[] accessKeys)
    {
        // insert & update new built-in access tokens
        var accessTokens = accessKeys.Select(Token.FromAccessKey);
        var clientProfiles = accessTokens.Select(token => ImportAccessToken(token, overwriteNewer: false, allowOverwriteBuiltIn: true, isBuiltIn: true));

        // remove old built-in client profiles that does not exist in the new list
        if (_clientProfiles.RemoveAll(x => x.IsBuiltIn && clientProfiles.All(y => y.ClientProfileId != x.ClientProfileId)) > 0)
            Save();

        return clientProfiles.ToArray();
    }

    public Token UpdateTokenByAccessKey(Token token, string accessKey)
    {
        try
        {
            var newToken = Token.FromAccessKey(accessKey);
            if (VhUtil.JsonEquals(token, newToken))
                return token;

            if (token.TokenId != newToken.TokenId)
                throw new Exception("Could not update the token via access key because its token ID is not the same.");

            // allow to overwrite builtIn because update token is from internal source and can update itself
            ImportAccessToken(newToken, overwriteNewer: true, allowOverwriteBuiltIn: true);
            VhLogger.Instance.LogInformation("ServerToken has been updated.");
            return newToken;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not update token from the given access-key.");
            return token;
        }

    }

    public async Task<bool> UpdateServerTokenByUrl(Token token)
    {
        if (string.IsNullOrEmpty(token.ServerToken.Url) || token.ServerToken.Secret == null)
            return false;

        // update token
        VhLogger.Instance.LogInformation("Trying to get a new ServerToken from url. Url: {Url}", VhLogger.FormatHostName(token.ServerToken.Url));
        try
        {
            using var client = new HttpClient();
            var encryptedServerToken = await VhUtil.RunTask(client.GetStringAsync(token.ServerToken.Url), TimeSpan.FromSeconds(20)).VhConfigureAwait();
            var newServerToken = ServerToken.Decrypt(token.ServerToken.Secret, encryptedServerToken);

            // return older only if token body is same and created time is newer
            if (!token.ServerToken.IsTokenUpdated(newServerToken))
            {
                VhLogger.Instance.LogInformation("The remote ServerToken is not new and has not been updated.");
                return false;
            }

            //update store
            token = VhUtil.JsonClone(token);
            token.ServerToken = newServerToken;
            ImportAccessToken(token, overwriteNewer: true, allowOverwriteBuiltIn: true);
            VhLogger.Instance.LogInformation("ServerToken has been updated from url.");
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

    internal void UpdateFromAccount(string[] accessKeys)
    {

        var accessTokens = accessKeys.Select(Token.FromAccessKey);

        // Remove client profiles that does not exist in the account
        var toRemoves = _clientProfiles
            .Where(x => x.IsForAccount)
            .Where(x => accessTokens.All(y => y.TokenId != x.Token.TokenId))
            .Select(x => x.ClientProfileId)
            .ToArray();

        foreach (var clientProfileId in toRemoves)
            Remove(clientProfileId);

        // Add or update access keys
        foreach (var accessKey in accessKeys)
            ImportAccessKey(accessKey, true);
    }
}