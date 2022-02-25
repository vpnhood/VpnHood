using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer;

public class CleanupManager
{
    private readonly ILogger<CleanupManager> _logger;
    private readonly object _isBusyLock = new();
    public bool IsBusy { get; private set; }

    public CleanupManager(ILogger<CleanupManager> logger)
    {
        _logger = logger;
    }

    public async Task Cleanup()
    {
        try
        {
            lock (_isBusyLock)
            {
                if (IsBusy) throw new Exception($"{nameof(CleanupManager)} is busy.");
                IsBusy = true;
            }

            await CleanupServerStatus();

        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CleanupServerStatus()
    {
        // find last server status id in report
        await using var vhReportContext = new VhReportContext();
        var serverStatus = await vhReportContext.ServerStatuses
            .OrderByDescending(x => x.ServerStatusId)
            .FirstOrDefaultAsync();
        if (serverStatus == null)
        {
            _logger.LogWarning("There is no item in the report database.");
            return;
        }

        // find old item
        while (true)
        {
            const int batchCount = 1000;

            await using var vhContext = new VhContext();
            vhContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
            _logger.LogInformation("Cleaning old ServerStatuses in agent database...");
            var sql = @$"
                DELETE TOP ({batchCount}) FROM {nameof(vhContext.ServerStatuses)} 
                WHERE {nameof(ServerStatusEx.IsLast)} = 0 and {nameof(ServerStatusEx.ServerStatusId)} <= {serverStatus.ServerStatusId}
                ";

            if (await vhContext.Database.ExecuteSqlRawAsync(sql) < batchCount)
                break;

            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }
}