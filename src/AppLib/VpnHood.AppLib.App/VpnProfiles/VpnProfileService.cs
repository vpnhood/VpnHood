using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.VpnProfiles;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Net.Toolkit.Exceptions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

using VpnHood.AppLib.App.DtoConverters;

namespace VpnHood.AppLib.App.VpnProfiles;

public class VpnProfileService
{
    private const string FilenameProfiles = "vpn_profiles.json";
    private List<VpnProfile> _vpnProfiles;
    private readonly Lock _updateByUrlLock = new();

    /// <summary>
    /// Owns the whole store: the in-memory list AND the file behind it. Every mutation, every read
    /// and <see cref="Save" /> itself run under it, so a mutate-then-write is one atomic act.
    /// <para>
    /// Without it two threads land here at once — the account refresh runs in the background while
    /// the UI edits a profile — and one meets the other's open file handle. That surfaces as an
    /// IOException from a background task on a device where nothing looks wrong, or worse, as a lost
    /// write. The lock is re-entrant, so the mutators may call Save() while already holding it.
    /// </para>
    /// </summary>
    private readonly Lock _storeLock = new();
    private readonly AppFeatures _appFeatures;
    private VpnProfileInfo? _cashInfo;
    private string? _cashInfoRegion;

    private string VpnProfilesFilePath => GetVpnProfilesFilePath(field);

    private static string GetVpnProfilesFilePath(string folderPath) => Path.Combine(folderPath, FilenameProfiles);

    public VpnProfileService(string folderPath, AppFeatures appFeatures)
    {
        VpnProfilesFilePath = folderPath ?? throw new ArgumentNullException(nameof(folderPath));
        _appFeatures = appFeatures;
        _vpnProfiles = [.. Load()];
    }

    public VpnProfileInfo? FindInfo(Guid vpnProfileId)
    {
        lock (_storeLock) {
            // the cached info bakes in the client country (policy & locations), so it is only valid
            // while the region it was built for is still the current one
            if (_cashInfo?.VpnProfileId == vpnProfileId &&
                _cashInfoRegion == AppRegionInfo.CurrentRegion.Name)
                return _cashInfo;

            var vpnProfile = FindById(vpnProfileId);
            _cashInfoRegion = AppRegionInfo.CurrentRegion.Name;
            _cashInfo = vpnProfile?.ToInfo(_appFeatures);
            return _cashInfo;
        }
    }

    public VpnProfileInfo GetInfo(Guid vpnProfileId)
    {
        return FindInfo(vpnProfileId)
               ?? throw new NotExistsException($"Could not find VpnProfile. VpnProfileId={vpnProfileId}");
    }

    public VpnProfile? FindById(Guid vpnProfileId)
    {
        lock (_storeLock)
            return _vpnProfiles.SingleOrDefault(x => x.VpnProfileId == vpnProfileId);
    }

    public VpnProfile? FindByTokenId(string tokenId)
    {
        lock (_storeLock)
            return _vpnProfiles.SingleOrDefault(x => x.Token.TokenId == tokenId);
    }

    public VpnProfile Get(Guid vpnProfileId)
    {
        return FindById(vpnProfileId)
               ?? throw new NotExistsException($"Could not find VpnProfile. VpnProfileId={vpnProfileId}");
    }

    public Token GetToken(string tokenId)
    {
        var vpnProfile = FindByTokenId(tokenId) ??
                            throw new NotExistsException($"TokenId does not exist. TokenId: {tokenId}");
        return vpnProfile.Token;
    }

    public VpnProfile[] List()
    {
        lock (_storeLock)
            return [.. _vpnProfiles];
    }

    public void Delete(Guid vpnProfileId)
    {
        lock (_storeLock) {
            var item =
                _vpnProfiles.SingleOrDefault(x => x.VpnProfileId == vpnProfileId)
                ?? throw new NotExistsException();

            // BuiltInToken should not be removed
            if (item.IsBuiltIn)
                throw new InvalidOperationException("Can not delete built-In tokens.");

            _vpnProfiles.Remove(item);
            Save();
        }
    }

    public void TryRemoveByTokenId(string tokenId)
    {
        lock (_storeLock) {
            var items = _vpnProfiles.Where(x => x.Token.TokenId == tokenId).ToArray();
            foreach (var item in items)
                _vpnProfiles.Remove(item);

            Save();
        }
    }

    private static IPEndPoint ParseEndPoint(string endpoint)
    {
        var ret = IPEndPoint.Parse(endpoint);

        // change port 0 to 443 as default
        if (ret.Port == 0)
            ret = new IPEndPoint(ret.Address, 443);

        return ret;
    }

    public VpnProfile Update(Guid vpnProfileId, VpnProfileUpdateParams updateParams)
    {
        lock (_storeLock) {
            var item = ApplyUpdate(vpnProfileId, updateParams);
            Save();
            return item;
        }
    }

    /// <summary>
    /// The account holds this code — it either ranked it for this device, or has just taken the one
    /// typed here — so the device owes no upload for it (keyring plan §6).
    /// <para>
    /// Deliberately NOT a field on <see cref="VpnProfileUpdateParams" />: those params are
    /// reachable from the web API, and anything able to claim <i>already synced</i> could make a code
    /// typed while the portal was blocked never reach the account at all. Only the account service
    /// knows this, and only it can say it.
    /// </para>
    /// </summary>
    public void SetAccountAccessCode(Guid vpnProfileId, string accessCode)
    {
        lock (_storeLock) {
            var item = ApplyUpdate(vpnProfileId,
                new VpnProfileUpdateParams { AccessCode = new Patch<string?>(accessCode) });

            // set even when the code did not change: that IS the upload landing on a code already here
            item.IsAccessCodeSynced = true;
            Save();
        }
    }

    private VpnProfile ApplyUpdate(Guid vpnProfileId, VpnProfileUpdateParams updateParams)
    {
        var item = FindById(vpnProfileId)
                   ?? throw new NotExistsException(
                       "VpnProfile does not exists. VpnProfileId: {vpnProfileId}");

        // update name
        if (updateParams.VpnProfileName != null) {
            var name = updateParams.VpnProfileName.Value?.Trim();
            if (name == item.Token.Name?.Trim()) name = null; // set default if the name is same as token name
            if (name?.Length == 0) name = null;
            item.VpnProfileName = name;
        }

        if (updateParams.IsFavorite != null)
            item.IsFavorite = updateParams.IsFavorite.Value;

        if (updateParams.CustomServerEndpoints != null)
            item.CustomServerEndpoints = updateParams.CustomServerEndpoints.Value?.Select(ParseEndPoint).ToArray();

        if (updateParams.IsCustomServerEndpointsEnabled != null)
            item.IsCustomServerEndpointsEnabled = updateParams.IsCustomServerEndpointsEnabled.Value;

        if (updateParams.CustomData != null)
            item.CustomData = updateParams.CustomData.Value;

        if (updateParams.IsPremiumLocationSelected != null)
            item.IsPremiumLocationSelected = updateParams.IsPremiumLocationSelected.Value;

        if (updateParams.SelectedLocation != null)
            item.SelectedLocation = updateParams.SelectedLocation;

        if (updateParams.AccessCode != null) {
            // compare the NORMALIZED forms. Validate strips the dashes, so the same code typed again
            // as "1614-2791-…" would otherwise read as a different credential and clear a refusal
            // that still applies to it.
            var accessCode = string.IsNullOrEmpty(updateParams.AccessCode.Value)
                ? null
                : AccessCodeUtils.Validate(updateParams.AccessCode.Value);

            if (accessCode != item.AccessCode) {
                item.AccessCode = accessCode;

                // this component's invariant, not a caller ritual: a code that appears here owes the
                // account an upload until somebody says otherwise, and clearing one owes nothing (§6)
                item.IsAccessCodeSynced = accessCode == null;

                // a different (or no) code is a different credential — the old refusal is not its story
                item.AccessCodeRefusal = null;

                // reset premium location selection if access code is removed
                if (item.AccessCode is null) {
                    item.IsPremiumLocationSelected = false;
                    item.SelectedLocation = null;
                }
            }
        }

        return item;
    }

    /// <summary>
    /// The access server refused this profile's code (keyring plan §8): KEEP the code and record the
    /// refusal. The profile goes on claiming premium — a refusal must never turn the build into its
    /// own free edition on nobody's decision — and what the mark buys instead is the truth: the app
    /// can say <i>expired</i> rather than <i>rejected</i>, and stay quiet at the next sign-in about a
    /// code the server has never heard of. Idempotent; the first refusal's story stands until the
    /// code changes or a connection succeeds.
    /// </summary>
    public void MarkAccessCodeRefused(Guid vpnProfileId, SessionErrorCode errorCode)
    {
        lock (_storeLock) {
            var item = FindById(vpnProfileId);
            if (item?.AccessCode == null || item.AccessCodeRefusal != null)
                return;

            item.AccessCodeRefusal = new AccessCodeRefusal { ErrorCode = errorCode.ToAppDto(), RefusedTime = FastDateTime.UtcNow };
            Save();
        }
    }

    /// <summary>
    /// A connection with this profile's code succeeded — revival proves itself (keyring plan §8):
    /// the refusal mark clears by itself, with nothing to re-enter.
    /// </summary>
    public void ClearAccessCodeRefused(Guid vpnProfileId)
    {
        lock (_storeLock) {
            var item = FindById(vpnProfileId);
            if (item?.AccessCodeRefusal == null)
                return;

            item.AccessCodeRefusal = null;
            Save();
        }
    }

    public VpnProfile ImportAccessKey(string accessKey)
    {
        try {
            var token = Token.FromAccessKey(accessKey);
            return ImportAccessToken(token, overwriteNewer: true, allowOverwriteBuiltIn: false);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not import access key.");
            throw;
        }
    }

    // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
    private VpnProfile ImportAccessToken(Token token, bool overwriteNewer,
        bool allowOverwriteBuiltIn,
        bool isBuiltIn = false)
    {
        lock (_storeLock) {
            // make sure no one overwrites built-in tokens
            if (!allowOverwriteBuiltIn && _vpnProfiles.Any(x => x.IsBuiltIn && x.Token.TokenId == token.TokenId))
                throw new UnauthorizedAccessException("Could not overwrite BuiltIn tokens.");

            // update tokens
            foreach (var item in _vpnProfiles.Where(vpnProfile =>
                         vpnProfile.Token.TokenId == token.TokenId)) {
                if (overwriteNewer || token.IssuedAt >= item.Token.IssuedAt)
                    item.Token = token;
            }

            // add if it is a new token
            if (_vpnProfiles.All(x => x.Token.TokenId != token.TokenId)) {
                var vpnProfile = new VpnProfile {
                    VpnProfileId = Guid.NewGuid(),
                    VpnProfileName = token.Name,
                    Token = token,
                    IsBuiltIn = isBuiltIn
                };

                _vpnProfiles.Add(vpnProfile);
            }

            // save profiles
            Save();

            var ret = _vpnProfiles.First(x => x.Token.TokenId == token.TokenId);
            return ret;
        }
    }

    internal VpnProfile[] ImportBuiltInAccessKeys(string[] accessKeys)
    {
        lock (_storeLock) {
            // insert & update new built-in access tokens
            var accessTokens = accessKeys.Select(Token.FromAccessKey);
            var vpnProfiles = accessTokens
                .Select(token =>
                    ImportAccessToken(token, overwriteNewer: false, allowOverwriteBuiltIn: true, isBuiltIn: true))
                .ToArray();

            // remove old built-in VPN profiles that does not exist in the new list
            if (_vpnProfiles.RemoveAll(x =>
                    x.IsBuiltIn && vpnProfiles.All(y => y.VpnProfileId != x.VpnProfileId)) > 0)
                Save();

            return vpnProfiles;
        }
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
                    VhLogger.Instance.LogInformation(
                        "The remote ServerToken is not new and has not been updated. Url: {Url}",
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
        lock (_storeLock) {
            Directory.CreateDirectory(Path.GetDirectoryName(VpnProfilesFilePath)!);
            File.WriteAllText(VpnProfilesFilePath, JsonSerializer.Serialize(_vpnProfiles));

            // clear cache
            _cashInfo = null;
        }
    }

    public void Reload()
    {
        lock (_storeLock) {
            _vpnProfiles = [.. Load()];
            _cashInfo = null;
        }
    }

    private IEnumerable<VpnProfile> Load()
    {
        try {
            var json = File.ReadAllText(VpnProfilesFilePath);
            var vpnProfiles = JsonUtils.Deserialize<VpnProfile[]>(json);
            return vpnProfiles;
        }
        catch {
            return [];
        }
    }
}