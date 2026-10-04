using System.Text.Json;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.Jobs;
using VpnHood.Net.Toolkit.Logging;
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
    private readonly Lock _fileLock = new();

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

    public SessionLocalData Get(ulong sessionId)
    {
        return Find(sessionId) ?? throw new SessionException(SessionErrorCode.AccessError,
            $"Could not get SessionId from session local data. SessionId: {sessionId}");
    }

    public SessionLocalData? Find(ulong sessionId)
    {
        lock (_fileLock) {
            // return null if file does not exist or any error in serialization
            var filePath = GetSessionFilePath(sessionId);
            if (!File.Exists(filePath))
                return null;

            // deserialize the file
            var sessionLocalData = JsonUtils.TryDeserializeFile<SessionLocalData>(filePath, logger: VhLogger.Instance);
            if (sessionLocalData == null) {
                VhUtils.TryDeleteFile(filePath);
                return null;
            }

            // update write time
            File.WriteAllText(filePath, JsonSerializer.Serialize(sessionLocalData));
            return sessionLocalData;
        }
    }

    public void Remove(ulong sessionId)
    {
        lock (_fileLock) {
            var filePath = GetSessionFilePath(sessionId);
            VhUtils.TryDeleteFile(filePath);
        }
    }

    public void Update(Session session)
    {
        var sessionLocalData = new SessionLocalData {
            SessionId = session.SessionId,
            ProtocolVersion = session.ProtocolVersion,
            VirtualIps = session.VirtualIps
        };

        lock (_fileLock) {
            var filePath = GetSessionFilePath(session.SessionId);
            File.WriteAllText(filePath, JsonSerializer.Serialize(sessionLocalData));
        }
    }

    // removes the records of sessions that left memory 7 days ago and were not asked for since. A session in memory
    // keeps its record, however old, and has it refreshed: a restart recovers the session from it
    internal ValueTask CleanupSessionFiles(CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        var files = Directory.GetFiles(_storagePath, $"*.{SessionFileExtension}");
        foreach (var file in files) {
            lock (_fileLock) {
                if (!File.Exists(file))
                    continue; // removed meanwhile

                if (IsSessionInMemory(file))
                    File.SetLastWriteTimeUtc(file, utcNow);
                else if (utcNow - File.GetLastWriteTimeUtc(file) > RecordLifetime)
                    VhUtils.TryDeleteFile(file);
            }
        }

        return default;
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