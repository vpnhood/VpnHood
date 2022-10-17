using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Persistence;

namespace VpnHood.AccessServer;

public class UsageCycleManager
{
    private readonly ILogger<UsageCycleManager> _logger;
    private readonly IServiceProvider _serviceProvider;
    private string? _lastCycleIdCache;
    private readonly object _isBusyLock = new();

    public string CurrentCycleId => DateTime.UtcNow.ToString("yyyy:MM");

    public bool IsBusy { get; private set; }

    public UsageCycleManager(ILogger<UsageCycleManager> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    private async Task ResetCycleTraffics(VhContext vhContext)
    {
        // it must be done by 1 hour
        vhContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(60));
        const string sql = @$"
                    UPDATE  {nameof(vhContext.Accesses)}
                       SET  {nameof(Access.CycleSentTraffic)} = 0, {nameof(Access.CycleReceivedTraffic)} = 0
                     WHERE {nameof(Access.CycleTraffic)} > 0
                    ";
        await vhContext.Database.ExecuteSqlRawAsync(sql);
    }

    public async Task DeleteCycle(string cycleId)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        await using var vhContext = scope.ServiceProvider.GetRequiredService<VhContext>();
        vhContext.PublicCycles.RemoveRange(await vhContext.PublicCycles.Where(e => e.PublicCycleId == cycleId).ToArrayAsync());
        await vhContext.SaveChangesAsync();
        _lastCycleIdCache = null;
    }

    public async Task UpdateCycle()
    {
        // check is current cycle already processed from cache
        if (_lastCycleIdCache == CurrentCycleId)
            return;

        try
        {
            lock (_isBusyLock) //todo convert to AsyncLock
            {
                if (IsBusy) throw new Exception($"{nameof(UsageCycleManager)} is busy.");
                IsBusy = true;
            }
            _logger.LogInformation($"Checking usage cycles for {CurrentCycleId}...");

            // check is current cycle already processed from db
            await using var scope = _serviceProvider.CreateAsyncScope();
            await using var vhContext = scope.ServiceProvider.GetRequiredService<VhContext>();
            if (await vhContext.PublicCycles.AnyAsync(e => e.PublicCycleId == CurrentCycleId))
            {
                _lastCycleIdCache = CurrentCycleId;
                return;
            }

            _logger.LogInformation($"Resetting usage cycles for {CurrentCycleId}...");

            await using var transaction = await vhContext.Database.BeginTransactionAsync();

            // add current cycle
            await vhContext.PublicCycles.AddAsync(new PublicCycle { PublicCycleId = CurrentCycleId });

            // reset usage for users
            await ResetCycleTraffics(vhContext);

            _lastCycleIdCache = CurrentCycleId;
            await vhContext.SaveChangesAsync();
            await vhContext.Database.CommitTransactionAsync();

            _logger.LogInformation($"All usage cycles for {CurrentCycleId} has been reset.");
        }
        finally
        {
            lock (_isBusyLock)
            {
                IsBusy = false;
            }
        }
    }
}