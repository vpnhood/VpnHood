using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server.Access;
using VpnHood.Core.Server.Access.Managers;
using VpnHood.Core.Server.Access.Messaging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Server;

/// <summary>
/// Sends the sessions' usage to the access manager, one request at a time, and applies each reply before the next
/// request goes. A failed request's usage merges back and goes with the next one, so an access manager back from
/// an outage gets one request, not a backlog. A reply lost after the access manager applied its request bills that
/// usage twice: rare, and accepted.
/// </summary>
internal class SessionUsageReporter(IAccessManager accessManager, ISessionResponseHandler sessionResponseHandler)
{
    // after a failed request, a sync waits this long to try again: an access manager that is down gets no
    // request from every bye
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    private readonly AsyncLock _sendLock = new();
    private readonly Lock _usagesLock = new();
    private readonly Dictionary<ulong, SessionUsage> _usages = new();
    private DateTime _retryTime = DateTime.MinValue;

    public void Add(ulong sessionId, Traffic traffic, SessionErrorCode errorCode = SessionErrorCode.Ok)
    {
        if (traffic.Total == 0 && errorCode == SessionErrorCode.Ok)
            return;

        lock (_usagesLock)
            Merge(new SessionUsage {
                SessionId = sessionId,
                Sent = traffic.Sent,
                Received = traffic.Received,
                ErrorCode = errorCode
            });
    }

    // the status goes every time, with all the pending usage
    public async Task<ServerCommand> SendStatus(ServerStatus status, CancellationToken cancellationToken)
    {
        using var lockResult = await _sendLock.LockAsync(cancellationToken).Vhc();
        status.SessionUsages = TakeUsages();
        var serverCommand = await UpdateStatus(status, cancellationToken).Vhc();
        sessionResponseHandler.ApplySessionResponses(serverCommand.SessionResponses);
        return serverCommand;
    }

    // the usage goes alone only if there is some and, unless forced, not right after a failed request;
    // null if it did not go
    public async Task<Dictionary<ulong, SessionResponse>?> SendUsages(bool force, CancellationToken cancellationToken)
    {
        using var lockResult = await _sendLock.LockAsync(cancellationToken).Vhc();
        if (!HasUsages() || (!force && DateTime.UtcNow < _retryTime))
            return null;

        var sessionResponses = await AddUsages(TakeUsages(), cancellationToken).Vhc();
        sessionResponseHandler.ApplySessionResponses(sessionResponses);
        return sessionResponses;
    }

    private async Task<ServerCommand> UpdateStatus(ServerStatus status, CancellationToken cancellationToken)
    {
        try {
            var serverCommand = await accessManager.Server_UpdateStatus(status, cancellationToken).Vhc();
            _retryTime = DateTime.MinValue;
            return serverCommand;
        }
        catch {
            MergeBack(status.SessionUsages);
            throw;
        }
    }

    private async Task<Dictionary<ulong, SessionResponse>> AddUsages(SessionUsage[] usages,
        CancellationToken cancellationToken)
    {
        try {
            var sessionResponses = await accessManager.Session_AddUsages(usages, cancellationToken).Vhc();
            _retryTime = DateTime.MinValue;
            return sessionResponses;
        }
        catch {
            MergeBack(usages);
            throw;
        }
    }

    // a failed request's usage goes with the next request
    private void MergeBack(SessionUsage[] usages)
    {
        _retryTime = DateTime.UtcNow + RetryDelay;
        lock (_usagesLock)
            foreach (var usage in usages)
                Merge(usage);
    }

    private bool HasUsages()
    {
        lock (_usagesLock)
            return _usages.Count > 0;
    }

    private SessionUsage[] TakeUsages()
    {
        lock (_usagesLock) {
            var usages = _usages.Values.ToArray();
            _usages.Clear();
            return usages;
        }
    }

    // a session's first close wins
    private void Merge(SessionUsage usage)
    {
        if (!_usages.TryGetValue(usage.SessionId, out var pendingUsage)) {
            _usages.Add(usage.SessionId, usage);
            return;
        }

        pendingUsage.Sent += usage.Sent;
        pendingUsage.Received += usage.Received;
        if (pendingUsage.ErrorCode == SessionErrorCode.Ok)
            pendingUsage.ErrorCode = usage.ErrorCode;
    }
}
