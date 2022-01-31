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
        var expirationTime = DateTime.UtcNow.AddHours(20000);


        // find old item
        await using var vhContext = new VhContext();
        vhContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(60));

        var serverStatus = await vhContext.ServerStatuses
            .OrderByDescending(x => x.CreatedTime)
            .FirstOrDefaultAsync(x => x.CreatedTime < expirationTime && !x.IsLast);
        if (serverStatus == null)
        {
            _logger.LogWarning($"There is no item in ServerStatus to clean. ExpirationTime: {expirationTime}");
            return;
        }

        // make sure it exists in Report Database
        await using var vhReportContext = new VhReportContext();
        if (!await vhReportContext.ServerStatuses.AnyAsync(x =>x.ServerStatusId == serverStatus.ServerStatusId))
        {
            _logger.LogWarning($"Old server statuses have not been copied to Report database! ServerStatusId: {serverStatus.ServerStatusId}");
            return;
        }

        // Delete expired records
        _logger.LogInformation("Cleaning old ServerStatuses in agent database...");
        var sql = @$"
                DELETE FROM {nameof(vhContext.ServerStatuses)} 
                WHERE {nameof(ServerStatusEx.CreatedTime)} < '{expirationTime.ToString("yyyy-MM-dd HH:mm:ss")}' and {nameof(ServerStatusEx.IsLast)} = 0
                ";
        await vhContext.Database.ExecuteSqlRawAsync(sql);
    }
}