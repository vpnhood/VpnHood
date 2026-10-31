using System.Text.Json;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Jobs;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Server;

internal class SessionLocalService : IDisposable
{
    private static readonly TimeSpan RecordLifetime = TimeSpan.FromDays(7);
    private readonly string _storagePath;
    private readonly IReadOnlyDictionary<ulong, Session> _sessions;
    private const string SessionFileExtension = "session";
    private readonly Job _cleanupSessionFilesJob;

    // a recovery and a removal may touch the same record at once: one file operation at a time, so neither fails
    // on a sharing violation nor reads a half-written record and deletes it as broken
    private readonly AsyncLock _fileLock = new();

    public SessionLocalService(string storagePath, IReadOnlyDictionary<ulong, Session> sessions)
    {
        _storagePath = storagePath;
        _sessions = sessions;
        Directory.CreateDirectory(storagePath);

        // the first run comes a day after the start, once the sessions to recover are back in memory
        _cleanupSessionFilesJob = new Job(CleanupSessionFiles,
            new JobOptions { Interval = TimeSpan.FromHours(24), Name = nameof(SessionLocalService) });
    }

    private string GetSessionFilePath(ulong sessionId)
    {
        return Path.Combine(_storagePath, $"{sessionId}.{SessionFileExtension}");
    }

    public async Task<SessionLocalData> Get(ulong sessionId, CancellationToken cancellationToken)
    {
        return await Find(sessionId, cancellationToken).Vhc() ?? throw new SessionException(
            SessionErrorCode.AccessError, $"Could not get SessionId from session local data. SessionId: {sessionId}");
    }

    public async Task<SessionLocalData?> Find(ulong sessionId, CancellationToken cancellationToken)
    {
        using var lockResult = await _fileLock.LockAsync(cancellationToken).Vhc();

        // null if the file does not exist or does not parse; a broken one goes
        var filePath = GetSessionFilePath(sessionId);
        var sessionLocalData =
            await JsonUtils.TryDeserializeFileAsync<SessionLocalData>(filePath, cancellationToken).Vhc();
        if (sessionLocalData == null) {
            VhUtils.TryDeleteFile(filePath);
            return null;
        }

        // update write time
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow);
        return sessionLocalData;
    }

    public async Task Remove(ulong sessionId, CancellationToken cancellationToken)
    {
        using var lockResult = await _fileLock.LockAsync(cancellationToken).Vhc();
        VhUtils.TryDeleteFile(GetSessionFilePath(sessionId));
    }

    public async Task Update(Session session, CancellationToken cancellationToken)
    {
        var sessionLocalData = new SessionLocalData {
            SessionId = session.SessionId,
            ProtocolVersion = session.ProtocolVersion,
            VirtualIps = session.VirtualIps
        };

        using var lockResult = await _fileLock.LockAsync(cancellationToken).Vhc();
        await FileUtils.WriteAllTextAtomicAsync(GetSessionFilePath(session.SessionId),
            JsonSerializer.Serialize(sessionLocalData), cancellationToken).Vhc();
    }

    // removes the records of sessions that left memory 7 days ago and were not asked for since. A session in memory
    // keeps its record, however old, and has it refreshed: a restart recovers the session from it
    internal async ValueTask CleanupSessionFiles(CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        var files = Directory.GetFiles(_storagePath, $"*.{SessionFileExtension}");
        foreach (var file in files) {
            using var lockResult = await _fileLock.LockAsync(cancellationToken).Vhc();
            if (!File.Exists(file))
                continue; // removed meanwhile

            if (IsSessionInMemory(file))
                File.SetLastWriteTimeUtc(file, utcNow);
            else if (utcNow - File.GetLastWriteTimeUtc(file) > RecordLifetime)
                VhUtils.TryDeleteFile(file);
        }
    }

    private bool IsSessionInMemory(string filePath)
    {
        return ulong.TryParse(Path.GetFileNameWithoutExtension(filePath), out var sessionId) &&
               _sessions.ContainsKey(sessionId);
    }

    public void Dispose()
    {
        _cleanupSessionFilesJob.Dispose();
    }
}