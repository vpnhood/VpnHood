using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Report.Services;

namespace VpnHood.AccessServer.Services;

public class SyncService
{
    public int BatchCount { get; set; } = 1000;
    private readonly ILogger<SyncService> _logger;
    private readonly ReportWriterService _reportWriterService;
    private readonly VhContext _vhContext;
    public SyncService(
        ILogger<SyncService> logger,
        ReportWriterService reportWriterService,
        VhContext vhContext)
    {
        _logger = logger;
        _reportWriterService = reportWriterService;
        _vhContext = vhContext;
    }

    public async Task Sync()
    {
        await SyncServerStatuses();
        await SyncAccessUsages();
        await SyncSessions();
    }

    private async Task SyncAccessUsages()
    {
        while (true)
        {
            // fetch new items
            _logger.LogTrace(AccessEventId.Archive, "Loading old AccessUsages from agent database...");
            var items = await _vhContext
                .AccessUsages
                .OrderBy(x => x.AccessUsageId)
                .Take(BatchCount)
                .AsNoTracking()
                .ToArrayAsync();

            if (!items.Any())
                return;

            // add to report
            await _reportWriterService.Write(items);

            // remove synced items
            _logger.LogInformation(AccessEventId.Archive, 
                "Removing old synced AccessUsages from agent database. Count: {Count}", items.Length);

            var ids = items.Select(x => x.AccessUsageId);
            await _vhContext.AccessUsages
                .Where(x => ids.Contains(x.AccessUsageId))
                .AsNoTracking()
                .ExecuteDeleteAsync();

            // next
            _vhContext.ChangeTracker.Clear();
            if (items.Length < BatchCount)
                break;

            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }

    private async Task SyncServerStatuses()
    {
        while (true)
        {
            // fetch new items
            _logger.LogTrace(AccessEventId.Archive, "Loading old ServerStatuses from agent database...");
            var items = await _vhContext
                .ServerStatuses
                .Where(x => !x.IsLast)
                .OrderBy(x => x.ServerStatusId)
                .Take(BatchCount)
                .ToArrayAsync();

            if (!items.Any())
                return;

            // add to report
            await _reportWriterService.Write(items);

            // remove synced items
            _logger.LogInformation(AccessEventId.Archive, "Removing old synced ServerStatuses from agent database. Count: {Count}", items.Length);
            var ids = items.Select(x => x.ServerStatusId);
            await _vhContext.ServerStatuses
                .Where(x => ids.Contains(x.ServerStatusId))
                .ExecuteDeleteAsync();

            _vhContext.ChangeTracker.Clear();
            if (items.Length < BatchCount)
                break;
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }

    private async Task SyncSessions()
    {
        while (true)
        {
            // fetch new items
            _logger.LogTrace(AccessEventId.Archive, "Loading old Sessions from agent database...");
            var items = await _vhContext
                .Sessions.Where(x => x.IsArchived)
                .OrderBy(x => x.SessionId)
                .Take(BatchCount)
                .ToArrayAsync();

            if (!items.Any())
                return;

            // add to report
            await _reportWriterService.Write(items);

            _logger.LogInformation(AccessEventId.Archive, $"Removing old synced Sessions from agent database. Count: {items.Length}");
            var ids = items.Select(x => x.SessionId);
            await _vhContext.Sessions
                .Where(x => ids.Contains(x.SessionId))
                .ExecuteDeleteAsync();
            
            _vhContext.ChangeTracker.Clear();
            if (items.Length < BatchCount)
                break;
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }

}