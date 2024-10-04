using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos.Devices;
using VpnHood.AccessServer.Report.Services;
using VpnHood.AccessServer.Repos;

namespace VpnHood.AccessServer.Services;

public class DevicesService(
    ReportUsageService reportUsageService,
    VhRepo vhRepo)
{
    public async Task<Device> GetByClientId(Guid projectId, Guid clientId)
    {
        var deviceModel = await vhRepo.DeviceGetByClientId(projectId, clientId);
        return deviceModel.ToDto();
    }

    public async Task<Device> Update(Guid projectId, Guid deviceId, DeviceUpdateParams updateParams)
    {
        var deviceModel = await vhRepo.DeviceGet(projectId, deviceId);
        if (updateParams.IsLocked != null)
            deviceModel.LockedTime = updateParams.IsLocked && deviceModel.LockedTime == null ? DateTime.UtcNow : null;

        await vhRepo.SaveChangesAsync();
        return deviceModel.ToDto();
    }

    public async Task<DeviceData[]> ListUsages(Guid projectId,
        Guid? accessTokenId = null, Guid? serverFarmId = null,
        DateTime? usageBeginTime = null, DateTime? usageEndTime = null,
        int recordIndex = 0, int recordCount = 100)
    {
        var usagesDictionary = await reportUsageService.GetDevicesUsage(projectId,
            accessTokenId: accessTokenId, serverFarmId: serverFarmId, usageBeginTime: usageBeginTime, usageEndTime: usageEndTime);

        var usages = usagesDictionary
            .OrderByDescending(x => x.Value.SentTraffic + x.Value.ReceivedTraffic)
            .Skip(recordIndex)
            .Take(recordCount)
            .Select(x => new {
                DeviceId = x.Key,
                Traffic = x.Value
            })
            .ToArray();

        var deviceIds = usages.Length < 500 ? usages.Select(x => x.DeviceId).ToArray() : null;
        var devices = await vhRepo.DeviceUsage(projectId: projectId, deviceIds: deviceIds,
            usageBeginTime: usageBeginTime, usageEndTime: usageEndTime);

        // create DeviceData
        var deviceDatas = new List<DeviceData>();
        foreach (var usage in usages)             if (devices.TryGetValue(usage.DeviceId, out var device))
                deviceDatas.Add(new DeviceData {
                    Device = device.ToDto(),
                    Usage = usage.Traffic
                });

        return deviceDatas.ToArray();
    }
}