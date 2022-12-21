using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Persistence;

namespace VpnHood.AccessServer.Services;

public class SyncService
{
    public int BatchCount { get; set; } = 1000;
    private readonly ILogger<SyncService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly object _isBusyLock = new();
    public bool IsBusy { get; private set; }

    public SyncService(ILogger<SyncService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task Sync()
    {
        try
        {
            lock (_isBusyLock)
            {
                if (IsBusy) throw new Exception($"{nameof(SyncService)} is busy.");
                IsBusy = true;
            }

            await SyncServerStatuses();
            await SyncAccessUsages();
            await SyncSessions();

        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SyncAccessUsages()
    {
        while (true)
        {
            // fetch new items
            await using var vhContextScope = _serviceProvider.CreateAsyncScope();
            await using var vhContext = vhContextScope.ServiceProvider.GetRequiredService<VhContext>();
            vhContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

            _logger.LogTrace("Loading old AccessUsages from agent database...");
            var items = await vhContext
                .AccessUsages
                .OrderBy(x => x.AccessUsageId)
                .Take(BatchCount)
                .ToArrayAsync();

            try
            {
                if (items.Length > 0)
                {
                    _logger.LogInformation($"Copy old AccessUsages to report database. Count: {items.Length}");
                    await using var vhReportContextScope = _serviceProvider.CreateAsyncScope();
                    await using var vhReportContext = vhReportContextScope.ServiceProvider.GetRequiredService<VhReportContext>();
                    vhReportContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

                    await vhReportContext.AccessUsages.AddRangeAsync(items);
                    await vhReportContext.SaveChangesAsync();
                }
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 })
            {
                // remove duplicates
                _logger.LogInformation("Managing duplicate AccessUsages...");
                await using var vhReportContextScope = _serviceProvider.CreateAsyncScope();
                await using var vhReportContext = vhReportContextScope.ServiceProvider.GetRequiredService<VhReportContext>();
                vhReportContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

                var ids = items.Select(x => x.AccessUsageId);
                var duplicates = await vhReportContext.AccessUsages.Where(x => ids.Contains(x.AccessUsageId)).ToArrayAsync();
                var items2 = items.Where(x => duplicates.All(y => x.AccessUsageId != y.AccessUsageId)).ToArray();
                if (items2.Any())
                {
                    await vhReportContext.AccessUsages.AddRangeAsync(items2);
                    await vhReportContext.SaveChangesAsync();
                }
            }

            // remove synced items
            if (items.Length > 0)
            {
                _logger.LogInformation($"Removing old synced items from agent database. Count: {items.Length}");
                var ids = string.Join(",", items.Select(x => x.AccessUsageId));
                var sql = @$"
                    DELETE FROM {nameof(vhContext.AccessUsages)} 
                    WHERE {nameof(AccessUsageModel.AccessUsageId)} in ({ids})
                    ";
                await vhContext.Database.ExecuteSqlRawAsync(sql);
            }

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
            await using var vhContextScope = _serviceProvider.CreateAsyncScope();
            await using var vhContext = vhContextScope.ServiceProvider.GetRequiredService<VhContext>();
            vhContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

            _logger.LogTrace("Loading old ServerStatuses from agent database...");
            var items = await vhContext
                .ServerStatuses
                .Where(x => !x.IsLast)
                .OrderBy(x => x.ServerStatusId)
                .Take(BatchCount)
                .ToArrayAsync();

            try
            {
                if (items.Length > 0)
                {
                    _logger.LogInformation($"Copy old ServerStatuses to report database. Count: {items.Length}");
                    await using var vhReportContextScope = _serviceProvider.CreateAsyncScope();
                    await using var vhReportContext = vhReportContextScope.ServiceProvider.GetRequiredService<VhReportContext>();
                    vhReportContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

                    await vhReportContext.ServerStatuses.AddRangeAsync(items);
                    await vhReportContext.SaveChangesAsync();
                }
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 })
            {
                // remove duplicates
                _logger.LogInformation("Managing duplicate ServerStatuses...");
                await using var vhReportContextScope = _serviceProvider.CreateAsyncScope();
                await using var vhReportContext = vhReportContextScope.ServiceProvider.GetRequiredService<VhReportContext>();
                vhReportContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

                var ids = items.Select(x => x.ServerStatusId);
                var duplicates = await vhReportContext.ServerStatuses.Where(x => ids.Contains(x.ServerStatusId)).ToArrayAsync();
                var items2 = items.Where(x => duplicates.All(y => x.ServerStatusId != y.ServerStatusId)).ToArray();
                if (items2.Any())
                {
                    await vhReportContext.ServerStatuses.AddRangeAsync(items2);
                    await vhReportContext.SaveChangesAsync();
                }
            }

            // remove synced items
            if (items.Length > 0)
            {
                _logger.LogInformation($"Removing old synced items from agent database. Count: {items.Length}");
                var ids = string.Join(",", items.Select(x => x.ServerStatusId));
                var sql = @$"
                    DELETE FROM {nameof(vhContext.ServerStatuses)} 
                    WHERE {nameof(ServerStatusModel.ServerStatusId)} in ({ids})
                    ";
                await vhContext.Database.ExecuteSqlRawAsync(sql);
            }

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
            await using var vhContextScope = _serviceProvider.CreateAsyncScope();
            await using var vhContext = vhContextScope.ServiceProvider.GetRequiredService<VhContext>();
            vhContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

            _logger.LogTrace("Loading old Sessions from agent database...");
            var items = await vhContext
                .Sessions.Where(x => x.IsArchived)
                .OrderBy(x => x.SessionId)
                .Take(BatchCount)
                .ToArrayAsync();

            try
            {
                if (items.Length > 0)
                {
                    _logger.LogInformation($"Copy old Sessions to report database. Count: {items.Length}");
                    await using var vhReportContextScope = _serviceProvider.CreateAsyncScope();
                    await using var vhReportContext = vhReportContextScope.ServiceProvider.GetRequiredService<VhReportContext>();
                    vhReportContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

                    await vhReportContext.Sessions.AddRangeAsync(items);
                    await vhReportContext.SaveChangesAsync();
                }
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 })
            {
                // remove duplicates
                _logger.LogInformation("Managing duplicate Sessions...");
                await using var vhReportContextScope = _serviceProvider.CreateAsyncScope();
                await using var vhReportContext = vhReportContextScope.ServiceProvider.GetRequiredService<VhReportContext>();
                vhReportContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

                var ids = items.Select(x => x.SessionId);
                var duplicates = await vhReportContext.Sessions.Where(x => ids.Contains(x.SessionId)).ToArrayAsync();
                var items2 = items.Where(x => duplicates.All(y => x.SessionId != y.SessionId)).ToArray();
                if (items2.Any())
                {
                    await vhReportContext.Sessions.AddRangeAsync(items2);
                    await vhReportContext.SaveChangesAsync();
                }
            }

            // remove synced items
            if (items.Length > 0)
            {
                _logger.LogInformation($"Removing old synced {nameof(VhContext.Sessions)} from agent database. Count: {items.Length}");
                var ids = string.Join(",", items.Select(x => x.SessionId));
                var sql = @$"
                    DELETE FROM {nameof(vhContext.Sessions)} 
                    WHERE {nameof(SessionModel.SessionId)} in ({ids})
                    ";
                await vhContext.Database.ExecuteSqlRawAsync(sql);
            }

            if (items.Length < BatchCount)
                break;
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }


}