using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Models;
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

    public async Task Write(ServerStatusModel[] items)
    {
        try
        {
            if (items.Length == 0)
                return;

            _logger.LogInformation(AccessEventId.Archive, $"Copy old ServerStatuses to report database. Count: {items.Length}");
            await _vhReportContext.ServerStatuses.AddRangeAsync(items);
            await _vhReportContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 })
        {
            // remove duplicates
            _logger.LogInformation(AccessEventId.Archive, "Managing duplicate ServerStatuses...");
            var ids = items.Select(x => x.ServerStatusId);
            var duplicates = await _vhReportContext.ServerStatuses.Where(x => ids.Contains(x.ServerStatusId)).ToArrayAsync();
            var items2 = items.Where(x => duplicates.All(y => x.ServerStatusId != y.ServerStatusId)).ToArray();
            if (items2.Any())
            {
                await _vhReportContext.ServerStatuses.AddRangeAsync(items);
                await _vhReportContext.SaveChangesAsync();
            }
        }
    }

    public async Task Write(SessionModel[] items)
    {
        try
        {
            if (items.Length == 0)
                return;

            _logger.LogInformation(AccessEventId.Archive, $"Copy old Sessions to report database. Count: {items.Length}");
            await _vhReportContext.Sessions.AddRangeAsync(items);
            await _vhReportContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 })
        {
            // remove duplicates
            _logger.LogInformation(AccessEventId.Archive, "Managing duplicate Sessions...");

            var ids = items.Select(x => x.SessionId);
            var duplicates = await _vhReportContext.Sessions.Where(x => ids.Contains(x.SessionId)).ToArrayAsync();
            var items2 = items.Where(x => duplicates.All(y => x.SessionId != y.SessionId)).ToArray();
            if (items2.Any())
            {
                await _vhReportContext.Sessions.AddRangeAsync(items2);
                await _vhReportContext.SaveChangesAsync();
            }
        }
    }

    public async Task Write(AccessUsageModel[] items)
    {
        try
        {
            if (items.Length == 0)
                return;

            _logger.LogInformation(AccessEventId.Archive, $"Copy old AccessUsages to report database. Count: {items.Length}");
            await _vhReportContext.AccessUsages.AddRangeAsync(items);
            await _vhReportContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 })
        {
            // remove duplicates
            _logger.LogInformation(AccessEventId.Archive, "Managing duplicate AccessUsages...");

            var ids = items.Select(x => x.AccessUsageId);
            var duplicates = await _vhReportContext.AccessUsages.Where(x => ids.Contains(x.AccessUsageId)).ToArrayAsync();
            var items2 = items.Where(x => duplicates.All(y => x.AccessUsageId != y.AccessUsageId)).ToArray();
            if (items2.Any())
            {
                await _vhReportContext.AccessUsages.AddRangeAsync(items2);
                await _vhReportContext.SaveChangesAsync();
            }
        }
    }
}