using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Threading.Tasks;
using VpnHood.AccessServer.Persistence;
using VpnHood.Common.JobController;

namespace VpnHood.AccessServer.Services;

public class MaintenanceService : IJob
{
    private readonly ILogger<MaintenanceService> _logger;
    private readonly VhContext _vhContext;
    private readonly VhContext _vhReportContext;
    private readonly JobSection _defragIndexJob;
    public JobSection JobSection { get; }


    public MaintenanceService(
        ILogger<MaintenanceService> logger,
        IOptions<AppOptions> appOptions,
        VhContext vhContext,
        VhContext vhReportContext
        )
    {
        _logger = logger;
        _vhContext = vhContext;
        _vhReportContext = vhReportContext;
        _defragIndexJob = new JobSection(appOptions.Value.DefragInterval);
        JobSection = new JobSection(appOptions.Value.AutoMaintenanceInterval ?? TimeSpan.MaxValue);
    }

    public async Task RunJob()
    {
        using var defragLock = _defragIndexJob.Enter();
        if (defragLock.ShouldEnter)
        {
            var task1 = DefragDatabaseIndexes(_vhContext, "Defragging Vh Indexes");
            var task2 = DefragDatabaseIndexes(_vhReportContext, "Defragging VhReport Indexes");
            await Task.WhenAll(task1, task2);
        }
    }

    private async Task DefragDatabaseIndexes(DbContext dbContext, string jobName)
    {
        try
        {
            _logger.LogInformation(AccessEventId.Maintenance, "Starting a job... JobName: {jobName}", jobName);
            dbContext.Database.SetCommandTimeout(TimeSpan.FromHours(5));
            var sql = await File.ReadAllTextAsync("SqlScripts/DefragIndexes.sql");
            await dbContext.Database.ExecuteSqlRawAsync(sql);

            _logger.LogInformation(AccessEventId.Maintenance, "Job completed... JobName: {jobName}", jobName);
        }
        catch (Exception ex)
        {
            _logger.LogError(AccessEventId.Maintenance, ex, "Could not complete a job. jobName: {jobName}", jobName);
        }
    }
}