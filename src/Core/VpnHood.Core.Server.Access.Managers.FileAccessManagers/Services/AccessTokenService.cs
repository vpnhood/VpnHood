using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server.Access.Managers.FileAccessManagers.Dtos;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Server.Access.Managers.FileAccessManagers.Services;

/// <summary>Provides a file access manager's access keys, each in a file of its own beside its usage.</summary>
public class AccessTokenService
{
    private const string FileExtAccessToken = ".token2";
    private const string FileExtAccessTokenUsage = ".usage";
    private readonly ConcurrentDictionary<string, AccessTokenData> _items = new();
    private readonly string _storagePath;

    internal AccessTokenService(string storagePath)
    {
        _storagePath = storagePath;
    }

    private string GetAccessTokenFileName(string tokenId)
    {
        // check is tokenId has any invalid file character
        if (tokenId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("invalid character int token id.");

        return Path.Combine(_storagePath, tokenId + FileExtAccessToken);
    }

    private string GetAccessTokenUsageFileName(string tokenId)
    {
        // check is tokenId has any invalid file character
        if (tokenId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("invalid character int token id.");

        return Path.Combine(_storagePath, tokenId + FileExtAccessTokenUsage);
    }

    /// <summary>Makes an access key, with a new ID and secret, and writes it to its file.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <param name="maxClientCount">The most devices that may use it at once, or 0 for no limit. The default is 1.</param>
    /// <param name="tokenName">Its name, or <see langword="null"/>.</param>
    /// <param name="maxTrafficByteCount">Its traffic quota, in bytes, or 0, the default, for none.</param>
    /// <param name="expirationTime">When it expires, in UTC, or <see langword="null"/>, the default, for never.</param>
    /// <param name="maxSpeedMbps">
    /// Its speed limits each way, in megabits a second, or <see langword="null"/>, the default, for none.
    /// </param>
    /// <param name="adRequirement">The ad it asks for (<see cref="AccessToken.AdRequirement"/>). The default is none.</param>
    /// <returns>A task whose result is the access key.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="IOException">The file could not be written.</exception>
    public async Task<AccessToken> Create(
        CancellationToken cancellationToken,
        int maxClientCount = 1,
        string? tokenName = null,
        int maxTrafficByteCount = 0,
        DateTime? expirationTime = null,
        Traffic? maxSpeedMbps = null,
        AdRequirement adRequirement = AdRequirement.None)
    {
        // generate key
        using var aes = Aes.Create();
        aes.KeySize = 128;
        aes.GenerateKey();

        // create AccessToken
        var accessToken = new AccessToken {
            TokenId = Guid.NewGuid().ToString(),
            IssuedAt = DateTime.UtcNow,
            MaxTraffic = maxTrafficByteCount,
            MaxSpeedMbps = maxSpeedMbps,
            MaxClientCount = maxClientCount,
            ExpirationTime = expirationTime,
            AdRequirement = adRequirement,
            Secret = aes.Key,
            Name = tokenName
        };

        // Write AccessToken
        await FileUtils.WriteAllTextAtomicAsync(GetAccessTokenFileName(accessToken.TokenId),
            JsonSerializer.Serialize(accessToken), cancellationToken).Vhc();

        return accessToken;
    }

    internal async Task<AccessTokenData[]> List(CancellationToken cancellationToken)
    {
        var files = Directory.GetFiles(_storagePath, "*" + FileExtAccessToken);
        var tokenItems = new List<AccessTokenData>();

        foreach (var file in files) {
            var tokenItem = await Find(Path.GetFileNameWithoutExtension(file), cancellationToken).Vhc();
            if (tokenItem != null)
                tokenItems.Add(tokenItem);
        }

        return [.. tokenItems];
    }

    /// <summary>Counts the access keys, by their files.</summary>
    /// <returns>A task whose result is the count.</returns>
    public Task<int> GetTotalCount()
    {
        var files = Directory.GetFiles(_storagePath, "*" + FileExtAccessToken);
        return Task.FromResult(files.Length);
    }

    /// <summary>Gets an access key with its usage, from the cache or else from its files.</summary>
    /// <param name="tokenId">The access key's ID.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the access key with its usage.</returns>
    /// <exception cref="KeyNotFoundException">There is no such access key.</exception>
    /// <exception cref="System.Text.Json.JsonException">
    /// The access key's file holds malformed JSON, or JSON that cannot be read as an access key.
    /// </exception>
    /// <exception cref="InvalidDataException">The access key's file holds <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="tokenId"/> has a character no file name may.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<AccessTokenData> Get(string tokenId, CancellationToken cancellationToken)
    {
        using var tokenLock = await AsyncLock.LockAsync(GetTokenLockName(tokenId), cancellationToken).Vhc();
        return await GetInternal(tokenId, cancellationToken).Vhc();
    }


    private async Task<AccessTokenData> GetInternal(string tokenId, CancellationToken cancellationToken)
    {
        // try get from cache
        if (_items.TryGetValue(tokenId, out var accessTokenData))
            return accessTokenData;

        // read access token record
        var tokenFileName = GetAccessTokenFileName(tokenId);
        if (!File.Exists(tokenFileName))
            throw new KeyNotFoundException($"Could not find tokenId. TokenId: {tokenId}");

        // try read token
        var tokenJson = await File.ReadAllTextAsync(tokenFileName, cancellationToken).Vhc();
        var accessToken = JsonUtils.Deserialize<AccessToken>(tokenJson);

        // try read usage
        var usageFileName = GetAccessTokenUsageFileName(tokenId);
        var usage = await JsonUtils.TryDeserializeFileAsync<AccessTokenUsage>(usageFileName, cancellationToken).Vhc() ??
                    new AccessTokenUsage { Version = 2 };

        // for backward compatibility
        if (File.Exists(usageFileName) && usage.Version < 2) {
            usage.CreatedTime = File.GetCreationTimeUtc(usageFileName);
            usage.LastUsedTime = File.GetLastWriteTimeUtc(usageFileName);
        }

        // create access token data
        accessTokenData = new AccessTokenData {
            AccessToken = accessToken,
            Usage = usage
        };

        // add to cache
        _items[tokenId] = accessTokenData;
        return accessTokenData;
    }

    internal async Task<AccessTokenData> Update(string tokenId, string tokenName, CancellationToken cancellationToken)
    {
        using var tokenLock = await AsyncLock.LockAsync(GetTokenLockName(tokenId), cancellationToken).Vhc();

        // a copy with the new name, cached once it is on disk: a cancel or a failed write leaves both as they were
        var accessTokenData = await GetInternal(tokenId, cancellationToken).Vhc();
        var accessToken = JsonUtils.Deserialize<AccessToken>(JsonSerializer.Serialize(accessTokenData.AccessToken));
        accessToken.Name = tokenName;
        await FileUtils.WriteAllTextAtomicAsync(GetAccessTokenFileName(tokenId), JsonSerializer.Serialize(accessToken),
            cancellationToken).Vhc();

        accessTokenData.AccessToken = accessToken;
        return accessTokenData;
    }


    internal async Task<AccessTokenData?> Find(string tokenId, CancellationToken cancellationToken)
    {
        try {
            return await Get(tokenId, cancellationToken).Vhc();
        }
        // a cancellation passes rather than reading as a missing token
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Failed to get token item. TokenId: {TokenId}", tokenId);
            return null;
        }
    }

    private static string GetTokenLockName(string tokenId) => $"token_{tokenId}";

    internal async Task AddUsage(string tokenId, Traffic traffic, CancellationToken cancellationToken)
    {
        //lock tokenId
        using var tokenLock = await AsyncLock.LockAsync(GetTokenLockName(tokenId), cancellationToken).Vhc();

        // the new usage, cached once it is on disk: a cancel or a failed write leaves both as they were
        var accessTokenData = await GetInternal(tokenId, cancellationToken).Vhc();
        var usage = new AccessTokenUsage {
            Version = 2,
            CreatedTime = accessTokenData.Usage.CreatedTime,
            LastUsedTime = DateTime.UtcNow,
            Sent = accessTokenData.Usage.Sent + traffic.Sent,
            Received = accessTokenData.Usage.Received + traffic.Received
        };
        await FileUtils.WriteAllTextAtomicAsync(GetAccessTokenUsageFileName(tokenId), JsonSerializer.Serialize(usage),
            cancellationToken).Vhc();

        accessTokenData.Usage = usage;
    }

    internal async Task Delete(string tokenId, CancellationToken cancellationToken)
    {
        // validate is it exists
        _ = await Find(tokenId, cancellationToken).Vhc()
            ?? throw new KeyNotFoundException("Could not find tokenId");

        // delete files
        using var tokenLock = await AsyncLock.LockAsync(GetTokenLockName(tokenId), cancellationToken).Vhc();

        // delete from cache
        _items.TryRemove(tokenId, out _);

        if (File.Exists(GetAccessTokenUsageFileName(tokenId)))
            File.Delete(GetAccessTokenUsageFileName(tokenId));

        if (File.Exists(GetAccessTokenFileName(tokenId)))
            File.Delete(GetAccessTokenFileName(tokenId));
    }
}