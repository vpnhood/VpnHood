using GrayMint.Common.AspNetCore.Jobs;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Report.Services;

namespace VpnHood.AccessServer.Services;

public class SyncService(
    ILogger<SyncService> logger,
    ReportWriterService reportWriterService,
    VhContext vhContext)
    : IGrayMintJob
{
    public int BatchCount { get; set; } = 1000;

    public async Task Sync()
    {
        logger.LogInformation("Start syncing the report database...");

        await SyncServerStatuses();
        await SyncAccessUsages();
        await SyncSessions();

        logger.LogInformation("Finish syncing the report database.");
    }

    public Task RunJob(CancellationToken cancellationToken)
    {
        return Sync();
    }

    private async Task SyncAccessUsages()
    {
        while (true) {
            // fetch new items
            logger.LogTrace("Loading old AccessUsages from agent database...");
            var items = await vhContext
                .AccessUsages
                .OrderBy(x => x.AccessUsageId)
                .Take(BatchCount)
                .AsNoTracking()
                .ToArrayAsync();

            if (!items.Any())
                return;

            // add to report
            await reportWriterService.Write(items.Select(x => x.ToArchive()));

            // remove synced items
            logger.LogInformation("Removing old synced AccessUsages from agent database. Count: {Count}", items.Length);

            var ids = items.Select(x => x.AccessUsageId);
            await vhContext.AccessUsages
                .Where(x => ids.Contains(x.AccessUsageId))
                .AsNoTracking()
                .ExecuteDeleteAsync();

            // next
            vhContext.ChangeTracker.Clear();
            if (items.Length < BatchCount)
                break;

            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }

    private async Task SyncServerStatuses()
    {
        while (true) {
            // fetch new items
            logger.LogTrace("Loading old ServerStatuses from agent database...");
            var items = await vhContext
                .ServerStatuses
                .Where(x => !x.IsLast)
                .Select(x => new {
                    x.Server!.ServerFarmId,
                    ServerStatus = x
                })
                .OrderBy(x => x.ServerStatus.ServerStatusId)
                .Take(BatchCount)
                .ToArrayAsync();

            if (!items.Any())
                return;

            // add to report
            await reportWriterService.Write(items.Select(x => x.ServerStatus.ToArchive(x.ServerFarmId)));

            // remove synced items
            logger.LogInformation("Removing old synced ServerStatuses from agent database. Count: {Count}",
                items.Length);
            var ids = items.Select(x => x.ServerStatus.ServerStatusId);
            await vhContext.ServerStatuses
                .Where(x => ids.Contains(x.ServerStatusId))
                .ExecuteDeleteAsync();

            vhContext.ChangeTracker.Clear();
            if (items.Length < BatchCount)
                break;
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }

    private async Task SyncSessions()
    {
        while (true) {
            // fetch new items
            logger.LogTrace("Loading old Sessions from agent database...");
            var items = await vhContext
                .Sessions.Where(x => x.IsArchived)
                .OrderBy(x => x.SessionId)
                .Take(BatchCount)
                .ToArrayAsync();

            if (!items.Any())
                return;

            // add to report
            await reportWriterService.Write(items.Select(x => x.ToArchive()));

            logger.LogInformation($"Removing old synced Sessions from agent database. Count: {items.Length}");
            var ids = items.Select(x => x.SessionId);
            await vhContext.Sessions
                .Where(x => ids.Contains(x.SessionId))
                .ExecuteDeleteAsync();

            vhContext.ChangeTracker.Clear();
            if (items.Length < BatchCount)
                break;
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }
}