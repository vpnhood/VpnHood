﻿using Microsoft.Extensions.Logging;
using System.Text.Json;
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

    public async Task<Token> UpdateTokenFromUrl(Token token)
    {
        if (string.IsNullOrEmpty(token.ServerToken.Url))
            return token;

        if (token.ServerToken.Secret == null)
        {
            // allow update from v3 to v4
            VhLogger.Instance.LogInformation("Trying to get new token from token url, Url: {Url}", token.ServerToken.Url);
            try
            {
                using var client = new HttpClient();
                var accessKey = await VhUtil.RunTask(client.GetStringAsync(token.ServerToken.Url), TimeSpan.FromSeconds(20));
                var newToken = Token.FromAccessKey(accessKey);
                if (newToken.TokenId != token.TokenId)
                {
                    VhLogger.Instance.LogInformation("Token has not been updated.");
                    return token;
                }

                //update store
                token = newToken;
                ImportAccessToken(token);
                VhLogger.Instance.LogInformation("Token has been updated. TokenId: {TokenId}, SupportId: {SupportId}",
                    VhLogger.FormatId(token.TokenId), VhLogger.FormatId(token.SupportId));
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(ex, "Could not update token from token url.");
            }
        }
        else
        {
            // update token v4
            VhLogger.Instance.LogInformation("Trying to get new server token from url. Url: {Url}", VhLogger.FormatHostName(token.ServerToken.Url));
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
                ImportAccessToken(token);
                VhLogger.Instance.LogInformation("ServerToken has been updated.");
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(ex, "Could not update ServerToken from url.");
            }
        }

        return token;
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ClientProfilesFilePath)!);
        File.WriteAllText(ClientProfilesFilePath, JsonSerializer.Serialize(_clientProfiles));
    }

    private ClientProfile[] Load()
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