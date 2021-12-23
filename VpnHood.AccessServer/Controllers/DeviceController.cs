using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId:guid}/devices")]
    public class DeviceController : SuperController<DeviceController>
    {
        public DeviceController(ILogger<DeviceController> logger) : base(logger)
        {
        }

        [HttpGet("{deviceId:guid}")]
        public async Task<DeviceData> Get(Guid projectId, Guid deviceId, DateTime? usageStartTime = null, DateTime? usageEndTime = null)
        {
            var ret = await SearchImpl(projectId, null, deviceId, usageStartTime, usageEndTime);
            return ret.Single();
        }

        [HttpGet("find-by-client")]
        public async Task<Device> FindByClientId(Guid projectId, Guid clientId)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ProjectRead);

            var ret = await vhContext.Devices
                .SingleAsync(x => x.ProjectId == projectId && x.ClientId == clientId);

            return ret;
        }

        [HttpPatch("{deviceId}")]
        public async Task<Device> Update(Guid projectId, Guid deviceId, DeviceUpdateParams updateParams)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.IpLockWrite);

            var device = await vhContext.Devices.SingleAsync(x => x.ProjectId == projectId && x.DeviceId == deviceId);
            if (updateParams.IsLocked != null) device.LockedTime = updateParams.IsLocked && device.LockedTime == null ? DateTime.UtcNow : null;

            var res = vhContext.Devices.Update(device);
            await vhContext.SaveChangesAsync();

            return res.Entity;
        }

        [HttpGet("search")]
        public Task<DeviceData[]> Search(Guid projectId, string? searchQuery = null, DateTime? usageStartTime = null, DateTime? usageEndTime = null, int recordIndex = 0, int recordCount = 101)
            => SearchImpl(projectId, searchQuery, null, usageStartTime, usageEndTime, recordIndex, recordCount);

        private async Task<DeviceData[]> SearchImpl(Guid projectId, string? searchQuery = null, Guid? deviceId = null, DateTime? usageStartTime = null, DateTime? usageEndTime = null, int recordIndex = 0, int recordCount = 101)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ProjectRead);

            usageStartTime ??= DateTime.UtcNow - TimeSpan.FromDays(30);
            usageEndTime ??= DateTime.UtcNow;

            var usages =
                from accessUsage in vhContext.AccessUsages
                where accessUsage.ProjectId == projectId &&
                    accessUsage.CreatedTime >= usageStartTime && accessUsage.CreatedTime <= usageEndTime &&
                    (deviceId == null || accessUsage.DeviceId == deviceId)
                orderby accessUsage.CreatedTime descending
                group accessUsage by new { accessUsage.DeviceId } into g
                select new
                {
                    DeviceId = (Guid?)g.Key.DeviceId,
                    SentTraffic = g.Sum(x => x.SentTraffic),
                    ReceivedTraffic = g.Sum(x => x.ReceivedTraffic),
                    LastUsedTime = g.Max(x => x.CreatedTime)
                };

            var query =
                from device in vhContext.Devices
                join usage in usages on device.DeviceId equals usage.DeviceId into grouping
                from usage in grouping.DefaultIfEmpty()
                where device.ProjectId == projectId &&
                    (deviceId == null || device.DeviceId == deviceId) && 
                    (string.IsNullOrEmpty(searchQuery) ||
                    device.DeviceId.ToString() == searchQuery ||
                    device.IpAddress == searchQuery ||
                    device.ClientId.ToString() == searchQuery)
                orderby usage.LastUsedTime descending
                select new DeviceData
                {
                    Device = device,
                    Usage = usage.DeviceId != null ? new TrafficUsage
                    {
                        LastUsedTime = usage.LastUsedTime,
                        ReceivedTraffic = usage.ReceivedTraffic,
                        SentTraffic = usage.SentTraffic
                    } : null
                };

            var res = await query
                .Skip(recordIndex)
                .Take(recordCount)
                .ToArrayAsync();
            return res;
        }
    }
}