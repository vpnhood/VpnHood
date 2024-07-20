using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using VpnHood.AccessServer.Report.Models;
using VpnHood.AccessServer.Report.Persistence;

namespace VpnHood.AccessServer.Report.Services;

public class ReportWriterService
{
    private readonly VhReportContext _vhReportContext;
    private readonly ILogger<ReportWriterService> _logger;

    public ReportWriterService(
        VhReportContext vhReportContext,
        ILogger<ReportWriterService> logger)
    {
        _vhReportContext = vhReportContext;
        _logger = logger;
        _vhReportContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
    }

    private static bool IsDuplicateKeyException(Exception ex)
    {
        return ex.InnerException is
            DbException { ErrorCode: 2627 } or
            PostgresException { SqlState: "23505" };
    }

    public async Task Write(IEnumerable<ServerStatusArchive> serverStatuses)
    {
        var items = serverStatuses.ToArray();

        try {
            if (items.Length == 0)
                return;

            foreach (var item in items)
                item.CreatedTime = DateTime.SpecifyKind(item.CreatedTime, DateTimeKind.Utc);

            _logger.LogInformation($"Copy old ServerStatuses to report database. Count: {items.Length}");
            _vhReportContext.ChangeTracker.Clear();
            await _vhReportContext.ServerStatuses.AddRangeAsync(items);
            await _vhReportContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex)) {
            // remove duplicates
            _logger.LogWarning("Managing duplicate ServerStatuses...");
            _vhReportContext.ChangeTracker.Clear();

            var ids = items.Select(x => x.ServerStatusId);
            var duplicates = await _vhReportContext.ServerStatuses.Where(x => ids.Contains(x.ServerStatusId))
                .ToArrayAsync();
            var items2 = items.Where(x => duplicates.All(y => x.ServerStatusId != y.ServerStatusId)).ToArray();
            if (items2.Any()) {
                await _vhReportContext.ServerStatuses.AddRangeAsync(items2);
                await _vhReportContext.SaveChangesAsync();
            }
        }
    }

    public async Task Write(IEnumerable<SessionArchive> sessions)
    {
        var items = sessions.ToArray();

        try {
            if (items.Length == 0)
                return;

            foreach (var item in items) {
                item.CreatedTime = DateTime.SpecifyKind(item.CreatedTime, DateTimeKind.Utc);
                item.LastUsedTime = DateTime.SpecifyKind(item.LastUsedTime, DateTimeKind.Utc);
                item.EndTime = item.EndTime != null ? DateTime.SpecifyKind(item.EndTime.Value, DateTimeKind.Utc) : null;
            }

            _logger.LogInformation($"Copy old Sessions to report database. Count: {items.Length}");
            _vhReportContext.ChangeTracker.Clear();
            await _vhReportContext.Sessions.AddRangeAsync(items);
            await _vhReportContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex)) {
            // remove duplicates
            _logger.LogInformation("Managing duplicate Sessions...");
            _vhReportContext.ChangeTracker.Clear();

            var ids = items.Select(x => x.SessionId);
            var duplicates = await _vhReportContext.Sessions.Where(x => ids.Contains(x.SessionId)).ToArrayAsync();
            var items2 = items.Where(x => duplicates.All(y => x.SessionId != y.SessionId)).ToArray();
            if (items2.Any()) {
                await _vhReportContext.Sessions.AddRangeAsync(items2);
                await _vhReportContext.SaveChangesAsync();
            }
        }
    }

    public async Task Write(IEnumerable<AccessUsageArchive> accessUsages)
    {
        var items = accessUsages.ToArray();

        try {
            if (items.Length == 0)
                return;

            foreach (var item in items)
                item.CreatedTime = DateTime.SpecifyKind(item.CreatedTime, DateTimeKind.Utc);

            _logger.LogInformation($"Copy old AccessUsages to report database. Count: {items.Length}");
            _vhReportContext.ChangeTracker.Clear();
            await _vhReportContext.AccessUsages.AddRangeAsync(items);
            await _vhReportContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex)) {
            // remove duplicates
            _logger.LogInformation("Managing duplicate AccessUsages...");
            _vhReportContext.ChangeTracker.Clear();

            var ids = items.Select(x => x.AccessUsageId);
            var duplicates = await _vhReportContext.AccessUsages.Where(x => ids.Contains(x.AccessUsageId))
                .ToArrayAsync();
            var items2 = items.Where(x => duplicates.All(y => x.AccessUsageId != y.AccessUsageId)).ToArray();
            if (items2.Any()) {
                await _vhReportContext.AccessUsages.AddRangeAsync(items2);
                await _vhReportContext.SaveChangesAsync();
            }
        }
    }
}