using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Tokens;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App.ClientProfiles;

public class ClientProfileService
{
    private const string FilenameProfiles = "vpn_profiles.json";
    private readonly string _folderPath;
    private List<ClientProfileItem> _clientProfileItems;
    private ClientProfileItem? _cache;
    private readonly object _updateByUrlLock = new();
    private string ClientProfilesFilePath => Path.Combine(_folderPath, FilenameProfiles);

    public ClientProfileService(string folderPath)
    {
        _folderPath = folderPath ?? throw new ArgumentNullException(nameof(folderPath));
        ClientProfileServiceLegacy.Migrate(folderPath, ClientProfilesFilePath);
        _clientProfileItems = Load().ToList();
    }

    public ClientProfileItem? FindById(Guid clientProfileId)
    {
        if (_cache?.ClientProfileId == clientProfileId)
            return _cache;

        _cache = _clientProfileItems.SingleOrDefault(x => x.ClientProfileId == clientProfileId);
        return _cache;
    }

    public ClientProfileItem? FindByTokenId(string tokenId)
    {
        if (_cache?.Token.TokenId == tokenId)
            return _cache;

        _cache = _clientProfileItems.SingleOrDefault(x => x.Token.TokenId == tokenId);
        return _cache;
    }

    public ClientProfileItem Get(Guid clientProfileId)
    {
        return FindById(clientProfileId)
               ?? throw new NotExistsException($"Could not find ClientProfile. ClientProfileId={clientProfileId}");
    }

    public Token GetToken(string tokenId)
    {
        var clientProfileItem = FindByTokenId(tokenId) ??
                            throw new NotExistsException($"TokenId does not exist. TokenId: {tokenId}");
        return clientProfileItem.Token;
    }

    public ClientProfileItem[] List()
    {
        return _clientProfileItems.ToArray();
    }

    public void Remove(Guid clientProfileId)
    {
        var item =
            _clientProfileItems.SingleOrDefault(x => x.ClientProfileId == clientProfileId)
            ?? throw new NotExistsException();

        // BuiltInToken should not be removed
        if (item.ClientProfile.IsBuiltIn)
            throw new UnauthorizedAccessException("Could not overwrite BuiltIn tokens.");

        _clientProfileItems.Remove(item);
        Save();
    }

    public void TryRemoveByTokenId(string tokenId)
    {
        var items = _clientProfileItems.Where(x => x.Token.TokenId == tokenId).ToArray();
        foreach (var item in items)
            _clientProfileItems.Remove(item);

        Save();
    }

    public ClientProfileItem Update(Guid clientProfileId, ClientProfileUpdateParams updateParams)
    {
        var item = _clientProfileItems.SingleOrDefault(x => x.ClientProfileId == clientProfileId)
                            ?? throw new NotExistsException(
                                "ClientProfile does not exists. ClientProfileId: {clientProfileId}");

        // update name
        if (updateParams.ClientProfileName != null) {
            var name = updateParams.ClientProfileName.Value?.Trim();
            if (name == item.Token.Name?.Trim()) name = null; // set default if the name is same as token name
            if (name?.Length == 0) name = null;
            item.ClientProfile.ClientProfileName = name;
        }

        if (updateParams.IsFavorite != null)
            item.ClientProfile.IsFavorite = updateParams.IsFavorite.Value;

        if (updateParams.CustomData != null)
            item.ClientProfile.CustomData = updateParams.CustomData.Value;

        if (updateParams.IsPremiumLocationSelected != null)
            item.ClientProfile.IsPremiumLocationSelected = updateParams.IsPremiumLocationSelected.Value;


        Save();
        return item;
    }

    public ClientProfileItem ImportAccessKey(string accessKey)
    {
        return ImportAccessKey(accessKey, false);
    }

    private ClientProfileItem ImportAccessKey(string accessKey, bool isForAccount)
    {
        var token = Token.FromAccessKey(accessKey);
        return ImportAccessToken(token, overwriteNewer: true, allowOverwriteBuiltIn: false, isForAccount: isForAccount);
    }

    private readonly object _importLock = new();
    // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
    private ClientProfileItem ImportAccessToken(Token token, bool overwriteNewer, bool allowOverwriteBuiltIn,
        bool isForAccount = false, bool isBuiltIn = false)
    {
        lock (_importLock) {

            // make sure no one overwrites built-in tokens
            if (!allowOverwriteBuiltIn && _clientProfileItems.Any(x => x.ClientProfile.IsBuiltIn && x.Token.TokenId == token.TokenId))
                throw new UnauthorizedAccessException("Could not overwrite BuiltIn tokens.");

            // update tokens
            foreach (var item in _clientProfileItems.Where(clientProfile =>
                         clientProfile.Token.TokenId == token.TokenId)) {
                if (overwriteNewer || token.IssuedAt >= item.Token.IssuedAt)
                    item.ClientProfile.Token = token;
            }

            // add if it is a new token
            if (_clientProfileItems.All(x => x.Token.TokenId != token.TokenId)) {
                var clientProfile = new ClientProfile{
                    ClientProfileId = Guid.NewGuid(),
                    ClientProfileName = token.Name,
                    Token = token,
                    IsForAccount = isForAccount,
                    IsBuiltIn = isBuiltIn
                };

                _clientProfileItems.Add(new ClientProfileItem(clientProfile));
            }

            // save profiles
            Save();

            var ret = _clientProfileItems.First(x => x.Token.TokenId == token.TokenId);
            return ret;
        }
    }

    internal ClientProfileItem[] ImportBuiltInAccessKeys(string[] accessKeys)
    {
        // insert & update new built-in access tokens
        var accessTokens = accessKeys.Select(Token.FromAccessKey);
        var clientProfiles = accessTokens.Select(token =>
            ImportAccessToken(token, overwriteNewer: false, allowOverwriteBuiltIn: true, isBuiltIn: true));

        // remove old built-in client profiles that does not exist in the new list
        if (_clientProfileItems.RemoveAll(x =>
                x.ClientProfile.IsBuiltIn && clientProfiles.All(y => y.ClientProfileId != x.ClientProfileId)) > 0)
            Save();

        return clientProfiles.ToArray();
    }

    public Token UpdateTokenByAccessKey(Token token, string accessKey)
    {
        try {
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
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not update token from the given access-key.");
            return token;
        }
    }

    public async Task<bool> UpdateServerTokenByUrls(Token token)
    {
        // run update for all urls asynchronously and return true if any of them is successful
        var urls = token.ServerToken.Urls;
        if (VhUtil.IsNullOrEmpty(urls) || token.ServerToken.Secret == null)
            return false;

        using var httpClient = new HttpClient();
        using var cts = new CancellationTokenSource();
        var tasks = urls
            .Select(url => UpdateServerTokenByUrl(token, url, httpClient, cts))
            .ToList();

        // wait for any of the tasks to complete successfully
        while (tasks.Count > 0) {
            var finishedTask = await Task.WhenAny(tasks).VhConfigureAwait();
            if (await finishedTask)
                return true;

            tasks.Remove(finishedTask);
        }

        return false;
    }

    private async Task<bool> UpdateServerTokenByUrl(Token token, string url,
        HttpClient httpClient, CancellationTokenSource cts)
    {
        if (VhUtil.IsNullOrEmpty(token.ServerToken.Urls) || token.ServerToken.Secret == null)
            return false;

        // update token
        VhLogger.Instance.LogInformation("Trying to get a new ServerToken from url. Url: {Url}",
            VhLogger.FormatHostName(url));

        try {
            var encryptedServerToken = await VhUtil
                    .RunTask(httpClient.GetStringAsync(url), TimeSpan.FromSeconds(20), cts.Token)
                    .VhConfigureAwait();

            // update token
            lock (_updateByUrlLock) {
                cts.Token.ThrowIfCancellationRequested();
                var newServerToken = ServerToken.Decrypt(token.ServerToken.Secret, encryptedServerToken);

                // return older only if token body is same and created time is newer
                if (!token.ServerToken.IsTokenUpdated(newServerToken)) {
                    VhLogger.Instance.LogInformation("The remote ServerToken is not new and has not been updated.");
                    return false;
                }

                //update store
                token = VhUtil.JsonClone(token);
                token.ServerToken = newServerToken;
                ImportAccessToken(token, overwriteNewer: true, allowOverwriteBuiltIn: true);
                VhLogger.Instance.LogInformation("ServerToken has been updated from url.");
                cts.Cancel();
                return true;
            }
        }
        catch (Exception ex) {
            if (!cts.IsCancellationRequested)
                VhLogger.Instance.LogError(ex, "Could not update ServerToken from url.");
            return false;
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ClientProfilesFilePath)!);
        var clientProfiles = _clientProfileItems.Select(x => x.ClientProfile).ToArray();
        File.WriteAllText(ClientProfilesFilePath, JsonSerializer.Serialize(clientProfiles));

        // clear cache
        _cache = null;
        foreach (var item in _clientProfileItems)
            item.Refresh();
    }

    public void Reload()
    {
        _clientProfileItems = Load().ToList();
    }

    private IEnumerable<ClientProfileItem> Load()
    {
        try {
            var json = File.ReadAllText(ClientProfilesFilePath);
            var clientProfiles = VhUtil.JsonDeserialize<ClientProfile[]>(json);
            return clientProfiles.Select(x => new ClientProfileItem(x));
        }
        catch {
            return [];
        }
    }

    internal void UpdateFromAccount(string[] accessKeys)
    {
        var accessTokens = accessKeys.Select(Token.FromAccessKey);

        // Remove client profiles that does not exist in the account
        var toRemoves = _clientProfileItems
            .Where(x => x.ClientProfile.IsForAccount)
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