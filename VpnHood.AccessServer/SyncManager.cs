using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer;

public class SyncManager
{
    private readonly ILogger<SyncManager> _logger;
    private readonly object _isBusyLock = new();
    public bool IsBusy { get; private set; }

    public SyncManager(ILogger<SyncManager> logger)
    {
        _logger = logger;
    }

    public async Task Sync()
    {
        try
        {
            lock (_isBusyLock)
            {
                if (IsBusy) throw new Exception($"{nameof(SyncManager)} is busy.");
                IsBusy = true;
            }

            await SyncServerStatus();

        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SyncServerStatus()
    {
        const int batchCount = 1000;

        // find last server status id in report
        await using var vhReportContext = new VhReportContext();
        var lastReportStatusId = (await vhReportContext.ServerStatuses
            .OrderByDescending(x => x.ServerStatusId)
            .FirstOrDefaultAsync())?.ServerStatusId ?? 0;

        // fetch new items
        await using var vhContext = new VhContext();
        vhContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

        while (true)
        {
            _logger.LogTrace("Loading new ServerStatuses from agent database...");
            var lastId = lastReportStatusId;
            var newItems = await vhContext
                .ServerStatuses.Where(x => x.ServerStatusId > lastId)
                .OrderBy(x => x.ServerStatusId)
                .Take(batchCount)
                .ToArrayAsync();

            if (newItems.Length > 0)
            {
                _logger.LogInformation($"Copy old ServerStatuses to report database. Count: {newItems.Length}");
                await vhReportContext.ServerStatuses.AddRangeAsync(newItems);
                await vhReportContext.SaveChangesAsync();
                lastReportStatusId = newItems.Last().ServerStatusId;
            }

            if (newItems.Length < batchCount) 
                break;
            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        // delete old items
        while (true)
        {

            _logger.LogTrace("Cleaning old ServerStatuses in agent database...");
            var sql = @$"
                DELETE TOP ({batchCount}) FROM {nameof(vhContext.ServerStatuses)} 
                WHERE {nameof(ServerStatusEx.IsLast)} = 0 and {nameof(ServerStatusEx.ServerStatusId)} <= {lastReportStatusId}
                ";

            if (await vhContext.Database.ExecuteSqlRawAsync(sql) < batchCount)
                break;

            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }
}