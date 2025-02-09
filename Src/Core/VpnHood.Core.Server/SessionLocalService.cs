using System.Text.Json;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Jobs;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Utils;

namespace VpnHood.Core.Server;

internal class SessionLocalService : IJob
{
    private readonly string _storagePath;
    public JobSection JobSection { get; } = new(TimeSpan.FromHours(24));
    private const string SessionFileExtension = "session";

    public SessionLocalService(string storagePath)
    {
        _storagePath = storagePath;
        Directory.CreateDirectory(storagePath);
        JobRunner.Default.Add(this);
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
        // return null if file does not exist or any error in serialization
        var filePath = GetSessionFilePath(sessionId);
        if (!File.Exists(filePath))
            return null;

        // deserialize the file
        var sessionLocalData = VhUtil.JsonDeserializeFile<SessionLocalData>(filePath, logger: VhLogger.Instance);
        if (sessionLocalData == null) {
            VhUtil.TryDeleteFile(filePath);
            return null;
        }

        // update write time
        File.WriteAllText(filePath, JsonSerializer.Serialize(sessionLocalData));
        return sessionLocalData;
    }

    public void Remove(ulong sessionId)
    {
        var filePath = GetSessionFilePath(sessionId);
        VhUtil.TryDeleteFile(filePath);
    }

    public void Update(Session session)
    {
        var sessionLocalData = new SessionLocalData {
            SessionId = session.SessionId,
            ProtocolVersion = session.ProtocolVersion,
            VirtualIps = session.VirtualIps
        };
        var filePath = GetSessionFilePath(session.SessionId);
        File.WriteAllText(filePath, JsonSerializer.Serialize(sessionLocalData));
    }

    private void Cleanup()
    {
        // remove all files that create for 7 days ago
        var utcNow = DateTime.UtcNow;
        var files = Directory.GetFiles(_storagePath, SessionFileExtension);
        foreach (var file in files) {
            var lastWriteTime = File.GetLastWriteTimeUtc(file);
            if (utcNow - lastWriteTime > TimeSpan.FromDays(7))
                VhUtil.TryDeleteFile(file);
        }
    }
    public Task RunJob()
    {
        Cleanup();
        return Task.CompletedTask;
    }

}