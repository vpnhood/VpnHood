﻿using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Exceptions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.ClientProfiles;

public class ClientProfileService
{
    private const string FilenameProfiles = "vpn_profiles.json";
    private readonly string _folderPath;
    private List<ClientProfile> _clientProfiles;
    private readonly object _updateByUrlLock = new();
    private ClientProfileInfo? _cashInfo;
    private string _clientCountryCode = RegionInfo.CurrentRegion.Name;

    private string ClientProfilesFilePath => Path.Combine(_folderPath, FilenameProfiles);

    public ClientProfileService(string folderPath)
    {
        _folderPath = folderPath ?? throw new ArgumentNullException(nameof(folderPath));
        _clientProfiles = Load().ToList();
    }

    public string ClientCountryCode {
        get => _clientCountryCode;
        set {
            if (_clientCountryCode == value)
                return;

            _clientCountryCode = value;
            Reload();
        }
    }


    public ClientProfileInfo? FindInfo(Guid clientProfileId)
    {
        if (_cashInfo?.ClientProfileId == clientProfileId)
            return _cashInfo;

        var clientProfile = FindById(clientProfileId);
        _cashInfo = clientProfile?.ToInfo();
        return _cashInfo;
    }

    public ClientProfileInfo GetInfo(Guid clientProfileId)
    {
        return FindInfo(clientProfileId)
               ?? throw new NotExistsException($"Could not find ClientProfile. ClientProfileId={clientProfileId}");
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
        return FindById(clientProfileId)
               ?? throw new NotExistsException($"Could not find ClientProfile. ClientProfileId={clientProfileId}");
    }

    public Token GetToken(string tokenId)
    {
        var clientProfile = FindByTokenId(tokenId) ??
                            throw new NotExistsException($"TokenId does not exist. TokenId: {tokenId}");
        return clientProfile.Token;
    }

    public ClientProfile[] List()
    {
        return _clientProfiles.ToArray();
    }

    public void Delete(Guid clientProfileId)
    {
        var item =
            _clientProfiles.SingleOrDefault(x => x.ClientProfileId == clientProfileId)
            ?? throw new NotExistsException();

        // BuiltInToken should not be removed
        if (item.IsBuiltIn)
            throw new UnauthorizedAccessException("Could not overwrite BuiltIn tokens.");

        _clientProfiles.Remove(item);
        Save();
    }

    public void TryRemoveByTokenId(string tokenId)
    {
        var items = _clientProfiles.Where(x => x.Token.TokenId == tokenId).ToArray();
        foreach (var item in items)
            _clientProfiles.Remove(item);

        Save();
    }

    public ClientProfile Update(Guid clientProfileId, ClientProfileUpdateParams updateParams)
    {
        var item = _clientProfiles.SingleOrDefault(x => x.ClientProfileId == clientProfileId)
                   ?? throw new NotExistsException(
                       "ClientProfile does not exists. ClientProfileId: {clientProfileId}");

        // update name
        if (updateParams.ClientProfileName != null) {
            var name = updateParams.ClientProfileName.Value?.Trim();
            if (name == item.Token.Name?.Trim()) name = null; // set default if the name is same as token name
            if (name?.Length == 0) name = null;
            item.ClientProfileName = name;
        }

        if (updateParams.IsFavorite != null)
            item.IsFavorite = updateParams.IsFavorite.Value;

        if (updateParams.CustomServerEndpoints != null)
            item.CustomServerEndpoints = updateParams.CustomServerEndpoints.Value?.Select(IPEndPoint.Parse).ToArray();

        if (updateParams.CustomData != null)
            item.CustomData = updateParams.CustomData.Value;

        if (updateParams.IsPremiumLocationSelected != null)
            item.IsPremiumLocationSelected = updateParams.IsPremiumLocationSelected.Value;

        if (updateParams.SelectedLocation != null)
            item.SelectedLocation = updateParams.SelectedLocation;

        if (updateParams.AccessCode != null)
            item.AccessCode = string.IsNullOrEmpty(updateParams.AccessCode.Value)
                ? null
                : AccessCodeUtils.Validate(updateParams.AccessCode.Value);

        Save();
        return item;
    }

    public ClientProfile ImportAccessKey(string accessKey)
    {
        return ImportAccessKey(accessKey, false);
    }

    private ClientProfile ImportAccessKey(string accessKey, bool isForAccount)
    {
        try {
            var token = Token.FromAccessKey(accessKey);
            return ImportAccessToken(token, overwriteNewer: true, allowOverwriteBuiltIn: false, isForAccount: isForAccount);

        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not import access key.");
            throw;
        }
    }

    private readonly object _importLock = new();

    // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
    private ClientProfile ImportAccessToken(Token token, bool overwriteNewer, bool allowOverwriteBuiltIn,
        bool isForAccount = false, bool isBuiltIn = false)
    {
        lock (_importLock) {
            // make sure no one overwrites built-in tokens
            if (!allowOverwriteBuiltIn && _clientProfiles.Any(x => x.IsBuiltIn && x.Token.TokenId == token.TokenId))
                throw new UnauthorizedAccessException("Could not overwrite BuiltIn tokens.");

            // update tokens
            foreach (var item in _clientProfiles.Where(clientProfile =>
                         clientProfile.Token.TokenId == token.TokenId)) {
                if (overwriteNewer || token.IssuedAt >= item.Token.IssuedAt)
                    item.Token = token;
            }

            // add if it is a new token
            if (_clientProfiles.All(x => x.Token.TokenId != token.TokenId)) {
                var clientProfile = new ClientProfile {
                    ClientProfileId = Guid.NewGuid(),
                    ClientProfileName = token.Name,
                    Token = token,
                    IsForAccount = isForAccount,
                    IsBuiltIn = isBuiltIn
                };

                _clientProfiles.Add(clientProfile);
            }

            // save profiles
            Save();

            var ret = _clientProfiles.First(x => x.Token.TokenId == token.TokenId);
            return ret;
        }
    }

    internal ClientProfile[] ImportBuiltInAccessKeys(string[] accessKeys)
    {
        // insert & update new built-in access tokens
        var accessTokens = accessKeys.Select(Token.FromAccessKey);
        var clientProfiles = accessTokens.Select(token =>
            ImportAccessToken(token, overwriteNewer: false, allowOverwriteBuiltIn: true, isBuiltIn: true));

        // remove old built-in client profiles that does not exist in the new list
        if (_clientProfiles.RemoveAll(x =>
                x.IsBuiltIn && clientProfiles.All(y => y.ClientProfileId != x.ClientProfileId)) > 0)
            Save();

        return clientProfiles.ToArray();
    }

    public bool TryUpdateTokenByAccessKey(string tokenId, string accessKey)
    {
        try {
            var token = GetToken(tokenId);
            var newToken = Token.FromAccessKey(accessKey);
            if (JsonUtils.JsonEquals(token, newToken))
                return false;

            if (token.TokenId != newToken.TokenId)
                throw new Exception("Could not update the token via access key because its token ID is not the same.");

            // allow to overwrite builtIn because update token is from internal source and can update itself
            ImportAccessToken(newToken, overwriteNewer: true, allowOverwriteBuiltIn: true);
            VhLogger.Instance.LogInformation("ServerToken has been updated.");
            return true;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not update token from the given access-key.");
            return false;
        }
    }

    public async Task<bool> UpdateServerTokenByUrls(Token token, CancellationToken cancellationToken)
    {
        // run update for all urls asynchronously and return true if any of them is successful
        var urls = token.ServerToken.Urls;
        if (VhUtils.IsNullOrEmpty(urls) || token.ServerToken.Secret == null)
            return false;

        using var httpClient = new HttpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = urls
            .Select(url => UpdateServerTokenByUrl(token, url, httpClient, cts))
            .ToList();

        // wait for any of the tasks to complete successfully
        while (tasks.Count > 0) {
            var finishedTask = await Task.WhenAny(tasks).Vhc();
            if (await finishedTask)
                return true;

            tasks.Remove(finishedTask);
        }

        return false;
    }

    private async Task<bool> UpdateServerTokenByUrl(Token token, string url,
        HttpClient httpClient, CancellationTokenSource cts)
    {
        try {
            if (VhUtils.IsNullOrEmpty(token.ServerToken.Urls) || token.ServerToken.Secret == null)
                return false;

            // update token
            VhLogger.Instance.LogInformation("Trying to get a new ServerToken from url. Url: {Url}",
                VhLogger.FormatHostName(url));

            var encryptedServerToken = await VhUtils
                .RunTask(httpClient.GetStringAsync(url), TimeSpan.FromSeconds(20), cts.Token)
                .Vhc();

            // update token
            lock (_updateByUrlLock) {
                cts.Token.ThrowIfCancellationRequested();
                var newServerToken = ServerToken.Decrypt(token.ServerToken.Secret, encryptedServerToken);

                // return if the token is not new
                if (!token.ServerToken.IsTokenUpdated(newServerToken)) {
                    VhLogger.Instance.LogInformation("The remote ServerToken is not new and has not been updated. Url: {Url}",
                        VhLogger.FormatHostName(url));
                    return false;
                }

                //update store
                token = JsonUtils.JsonClone(token);
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
        File.WriteAllText(ClientProfilesFilePath, JsonSerializer.Serialize(_clientProfiles));

        // clear cache
        _cashInfo = null;
    }

    public void Reload()
    {
        _clientProfiles = Load().ToList();
    }

    private IEnumerable<ClientProfile> Load()
    {
        try {
            var json = File.ReadAllText(ClientProfilesFilePath);
            var clientProfiles = JsonUtils.Deserialize<ClientProfile[]>(json);
            return clientProfiles;
        }
        catch {
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
            Delete(clientProfileId);

        // Add or update access keys
        foreach (var accessKey in accessKeys)
            ImportAccessKey(accessKey, true);
    }
}