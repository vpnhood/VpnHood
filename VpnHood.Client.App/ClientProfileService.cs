using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Common;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;
using VpnHood.Tunneling;

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

    public bool UpdateServerToken(string tokenId, string? serverTokenUrl, byte[]? serverTokenSecret)
    {
        // find client profile
        var clientProfile = FindByTokenId(tokenId);
        if (clientProfile == null || VhUtil.IsNullOrEmpty(serverTokenSecret))
            return false;

        // return if url not secret is changed
        var serverToken = clientProfile.Token.ServerToken;
        if (serverToken.Url == serverTokenUrl && serverTokenSecret.SequenceEqual(serverToken.Secret ?? []))
            return false;

        if (!Uri.TryCreate(serverTokenUrl, UriKind.Absolute, out _))
        {
            VhLogger.Instance.LogWarning(GeneralEventId.Session, "Could not update ServerToken Url because it is not valid. ServerTokenUrl: {ServerTokenUrl}",
                VhLogger.FormatHostName(serverTokenUrl));
            return false;
        }

        // log
        VhLogger.Instance.LogInformation(GeneralEventId.Session, "Updating ServerToken Url. ServerTokenUrl: {ServerTokenUrl}",
            VhLogger.FormatHostName(serverTokenUrl));

        // update client profile
        clientProfile.Token.ServerToken.Url = serverTokenUrl;
        clientProfile.Token.ServerToken.Secret = serverTokenSecret;
        Update(clientProfile);
        return true;
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
        if (string.IsNullOrEmpty(token.ServerToken.Url) || token.ServerToken.Secret == null)
            return token;

        // update token
        VhLogger.Instance.LogInformation("Trying to get new server token from url. Url: {Url}", VhLogger.FormatHostName(token.ServerToken.Url));
        try
        {
            using var client = new HttpClient();
            var encryptedServerToken = await VhUtil.RunTask(client.GetStringAsync(token.ServerToken.Url), TimeSpan.FromSeconds(20));
            var newServerToken = ServerToken.Decrypt(token.ServerToken.Secret, encryptedServerToken);

            // return older only if token body is same and created time is newer
            if (!token.ServerToken.IsTokenUpdated(newServerToken))
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

        return token;
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