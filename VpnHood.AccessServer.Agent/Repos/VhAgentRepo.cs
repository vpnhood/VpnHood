using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.Common;

namespace VpnHood.AccessServer.Agent.Repos;

public class VhAgentRepo(VhContext vhContext, ILogger<VhAgentRepo> logger)
{
    public void ClearChangeTracker()
    {
        vhContext.ChangeTracker.Clear();
    }

    public void SetCommandTimeout(TimeSpan fromMinutes)
    {
        vhContext.Database.SetCommandTimeout(fromMinutes);
    }

    public async Task<InitCache> GetInitView(DateTime minServerUsedTime)
    {
        vhContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
        var autoServerLocation = ServerLocationInfo.Auto;

        // Statuses. Load Deleted Servers and Projects too but filter by minServerUsedTime
        logger.LogInformation("Loading the recent server status, farms and projects ...");
        var statuses = await vhContext.ServerStatuses
            .Where(x => x.IsLast && x.CreatedTime > minServerUsedTime)
            .Select(x => new {
                Server = new ServerCache {
                    ProjectId = x.ProjectId,
                    ServerId = x.ServerId,
                    ServerName = x.Server!.ServerName,
                    ServerFarmId = x.Server!.ServerFarmId,
                    Version = x.Server!.Version,
                    LastConfigError = x.Server!.LastConfigError,
                    LastConfigCode = x.Server!.LastConfigCode,
                    ConfigCode = x.Server!.ConfigCode,
                    ConfigureTime = x.Server!.ConfigureTime,
                    IsEnabled = x.Server!.IsEnabled,
                    AuthorizationCode = x.Server!.AuthorizationCode,
                    AccessPoints = x.Server.AccessPoints.ToArray(),
                    ServerFarmName = x.Server.ServerFarm!.ServerFarmName,
                    ServerProfileId = x.Server.ServerFarm!.ServerProfileId,
                    ServerStatus = x,
                    LocationInfo = x.Server.Location != null
                        ? ServerLocationInfo.Parse(x.Server.Location.ToPath())
                        : autoServerLocation,
                    AllowInAutoLocation = x.Server!.AllowInAutoLocation,
                    LogicalCoreCount = x.Server.LogicalCoreCount ?? 1,
                    Power = x.Server.Power
                },
                Farm = new ServerFarmCache {
                    ProjectId = x.Server.ServerFarm.ProjectId,
                    ServerFarmId = x.Server.ServerFarm.ServerFarmId,
                    ServerFarmName = x.Server.ServerFarm.ServerFarmName,
                    TokenJson = x.Server.ServerFarm.TokenJson,
                    PushTokenToClient = x.Server.ServerFarm.PushTokenToClient
                },
                Project = new ProjectCache {
                    GaMeasurementId = x.Server.Project!.GaMeasurementId,
                    ProjectId = x.Server.ProjectId,
                    GaApiSecret = x.Server.Project.GaApiSecret,
                    ProjectName = x.Server.Project.ProjectName,
                    AdRewardSecret = x.Server.Project.AdRewardSecret
                }
            })
            .AsNoTracking()
            .ToArrayAsync();

        // Sessions
        logger.LogInformation("Loading the sessions and accesses ...");
        var sessionsQuery = vhContext.Sessions
            .Where(session => !session.IsArchived)
            .Select(x => new {
                Session = new SessionCache {
                    ProjectId = x.ProjectId,
                    SessionId = x.SessionId,
                    AccessId = x.AccessId,
                    ServerId = x.ServerId,
                    DeviceId = x.DeviceId,
                    ExtraData = x.ExtraData,
                    CreatedTime = x.CreatedTime,
                    LastUsedTime = x.LastUsedTime,
                    AdExpirationTime = x.AdExpirationTime,
                    ClientVersion = x.ClientVersion,
                    EndTime = x.EndTime,
                    ErrorCode = x.ErrorCode,
                    ErrorMessage = x.ErrorMessage,
                    SessionKey = x.SessionKey,
                    SuppressedBy = x.SuppressedBy,
                    SuppressedTo = x.SuppressedTo,
                    IsArchived = x.IsArchived,
                    ClientId = x.Device!.ClientId,
                    UserAgent = x.Device.UserAgent,
                    Country = x.Device.Country,
                    DeviceIp = x.DeviceIp,
                    IsAdReward = x.IsAdReward
                },
                Access = new AccessCache {
                    AccessId = x.AccessId,
                    ExpirationTime = x.Access!.AccessToken!.ExpirationTime,
                    DeviceId = x.Access.DeviceId,
                    LastUsedTime = x.Access.LastUsedTime,
                    Description = x.Access.Description,
                    LastCycleSentTraffic = x.Access.LastCycleSentTraffic,
                    LastCycleReceivedTraffic = x.Access.LastCycleReceivedTraffic,
                    LastCycleTraffic = x.Access.LastCycleTraffic,
                    TotalSentTraffic = x.Access.TotalSentTraffic,
                    TotalReceivedTraffic = x.Access.TotalReceivedTraffic,
                    TotalTraffic = x.Access.TotalTraffic,
                    CycleTraffic = x.Access.CycleTraffic,
                    AccessTokenId = x.Access.AccessTokenId,
                    CreatedTime = x.Access.CreatedTime,
                    AccessTokenSupportCode = x.Access.AccessToken.SupportCode,
                    AccessTokenName = x.Access.AccessToken.AccessTokenName,
                    MaxDevice = x.Access.AccessToken.MaxDevice,
                    MaxTraffic = x.Access.AccessToken.MaxTraffic,
                    IsPublic = x.Access.AccessToken.IsPublic,
                    IsAccessTokenEnabled = !x.Access.AccessToken.IsDeleted && x.Access.AccessToken.IsEnabled
                }
            })
            .AsNoTracking();

        var sessions = await sessionsQuery
            .ToArrayAsync();

        var ret = new InitCache {
            Servers = statuses.Select(x => x.Server).ToArray(),
            Farms = statuses.Select(x => x.Farm).DistinctBy(x => x.ServerFarmId).ToArray(),
            Projects = statuses.Select(x => x.Project).DistinctBy(x => x.ProjectId).ToArray(),
            Sessions = sessions.Select(x => x.Session).ToArray(),
            Accesses = sessions.Select(x => x.Access).DistinctBy(x => x.AccessId).ToArray()
        };

        return ret;
    }

    public Task<ServerCache[]> ServersGet(Guid[]? serverIds = null)
    {
        var autoServerLocation = ServerLocationInfo.Auto;
        return vhContext.Servers
            .Where(x => serverIds == null || serverIds.Contains(x.ServerId))
            .Include(x => x.ServerStatuses!.Where(y => y.IsLast == true))
            .Select(x => new ServerCache {
                ProjectId = x.ProjectId,
                ServerId = x.ServerId,
                ServerFarmId = x.ServerFarmId,
                ServerName = x.ServerName,
                Version = x.Version,
                LastConfigError = x.LastConfigError,
                LastConfigCode = x.LastConfigCode,
                ConfigCode = x.ConfigCode,
                ConfigureTime = x.ConfigureTime,
                IsEnabled = x.IsEnabled,
                AuthorizationCode = x.AuthorizationCode,
                AccessPoints = x.AccessPoints.ToArray(),
                ServerFarmName = x.ServerFarm!.ServerFarmName,
                ServerProfileId = x.ServerFarm!.ServerProfileId,
                ServerStatus = x.ServerStatuses!.FirstOrDefault(),
                LocationInfo = x.Location != null ? ServerLocationInfo.Parse(x.Location.ToPath()) : autoServerLocation,
                AllowInAutoLocation = x.AllowInAutoLocation,
                LogicalCoreCount = x.LogicalCoreCount ?? 1,
                Power = x.Power
            })
            .AsNoTracking()
            .ToArrayAsync();
    }


    public async Task<ServerCache> ServerGet(Guid serverId)
    {
        var server = await ServersGet([serverId]);
        return server.Single();
    }

    public Task<ProjectCache> ProjectGet(Guid projectId)
    {
        return vhContext.Projects
            .Where(project => project.ProjectId == projectId)
            .Select(project => new ProjectCache {
                ProjectId = project.ProjectId,
                ProjectName = project.ProjectName,
                GaMeasurementId = project.GaMeasurementId,
                GaApiSecret = project.GaApiSecret,
                AdRewardSecret = project.AdRewardSecret
            })
            .AsNoTracking()
            .SingleAsync();
    }

    public async Task<AccessCache> AccessGet(Guid accessId)
    {
        return await vhContext.Accesses
            .Where(x => x.AccessId == accessId)
            .Select(x => new AccessCache {
                AccessId = x.AccessId,
                DeviceId = x.DeviceId,
                LastUsedTime = x.LastUsedTime,
                Description = x.Description,
                LastCycleSentTraffic = x.LastCycleSentTraffic,
                LastCycleReceivedTraffic = x.LastCycleReceivedTraffic,
                LastCycleTraffic = x.LastCycleTraffic,
                TotalSentTraffic = x.TotalSentTraffic,
                TotalReceivedTraffic = x.TotalReceivedTraffic,
                TotalTraffic = x.TotalTraffic,
                CycleTraffic = x.CycleTraffic,
                AccessTokenId = x.AccessTokenId,
                CreatedTime = x.CreatedTime,
                ExpirationTime = x.AccessToken!.ExpirationTime,
                AccessTokenSupportCode = x.AccessToken.SupportCode,
                AccessTokenName = x.AccessToken.AccessTokenName,
                MaxDevice = x.AccessToken.MaxDevice,
                MaxTraffic = x.AccessToken.MaxTraffic,
                IsPublic = x.AccessToken.IsPublic,
                IsAccessTokenEnabled = !x.AccessToken.IsDeleted && x.AccessToken.IsEnabled
            })
            .AsNoTracking()
            .SingleAsync();
    }

    public async Task<AccessCache?> AccessFind(Guid accessTokenId, Guid? deviceId)
    {
        return await vhContext.Accesses
            .Where(x => x.AccessTokenId == accessTokenId && x.DeviceId == deviceId)
            .Select(x => new AccessCache {
                AccessId = x.AccessId,
                DeviceId = x.DeviceId,
                LastUsedTime = x.LastUsedTime,
                Description = x.Description,
                LastCycleSentTraffic = x.LastCycleSentTraffic,
                LastCycleReceivedTraffic = x.LastCycleReceivedTraffic,
                LastCycleTraffic = x.LastCycleTraffic,
                TotalSentTraffic = x.TotalSentTraffic,
                TotalReceivedTraffic = x.TotalReceivedTraffic,
                TotalTraffic = x.TotalTraffic,
                CycleTraffic = x.CycleTraffic,
                AccessTokenId = x.AccessTokenId,
                CreatedTime = x.CreatedTime,
                ExpirationTime = x.AccessToken!.ExpirationTime,
                AccessTokenSupportCode = x.AccessToken.SupportCode,
                AccessTokenName = x.AccessToken.AccessTokenName,
                MaxDevice = x.AccessToken.MaxDevice,
                MaxTraffic = x.AccessToken.MaxTraffic,
                IsPublic = x.AccessToken.IsPublic,
                IsAccessTokenEnabled = !x.AccessToken.IsDeleted && x.AccessToken.IsEnabled
            })
            .AsNoTracking()
            .SingleOrDefaultAsync();
    }

    public async Task<AccessCache> AccessAdd(Guid accessTokenId, Guid? deviceId)
    {
        var accessToken = await vhContext.AccessTokens
            .Where(x => x.AccessTokenId == accessTokenId)
            .SingleAsync();

        var access = new AccessCache {
            AccessId = Guid.NewGuid(),
            AccessTokenId = accessTokenId,
            DeviceId = deviceId,
            CreatedTime = DateTime.UtcNow,
            LastUsedTime = DateTime.UtcNow,
            Description = null,
            LastCycleSentTraffic = 0,
            LastCycleReceivedTraffic = 0,
            LastCycleTraffic = 0,
            TotalSentTraffic = 0,
            TotalReceivedTraffic = 0,
            TotalTraffic = 0,
            CycleTraffic = 0,
            ExpirationTime = accessToken.ExpirationTime,
            AccessTokenSupportCode = accessToken.SupportCode,
            AccessTokenName = accessToken.AccessTokenName,
            MaxDevice = accessToken.MaxDevice,
            MaxTraffic = accessToken.MaxTraffic,
            IsPublic = accessToken.IsPublic,
            IsAccessTokenEnabled = accessToken is { IsDeleted: false, IsEnabled: true }
        };

        await vhContext.Accesses.AddAsync(access.ToModel());
        return access;
    }

    public void SessionUpdate(SessionCache session)
    {
        var model = new SessionModel {
            SessionId = session.SessionId,
            ProjectId = session.ProjectId,
            AccessId = session.AccessId,
            ServerId = session.ServerId,
            DeviceId = session.DeviceId,
            ExtraData = session.ExtraData,
            CreatedTime = session.CreatedTime,
            LastUsedTime = session.LastUsedTime,
            ClientVersion = session.ClientVersion,
            EndTime = session.EndTime,
            ErrorCode = session.ErrorCode,
            ErrorMessage = session.ErrorMessage,
            SessionKey = session.SessionKey,
            SuppressedBy = session.SuppressedBy,
            SuppressedTo = session.SuppressedTo,
            IsArchived = session.IsArchived,
            Country = session.Country,
            DeviceIp = session.DeviceIp,
            AdExpirationTime = session.AdExpirationTime,
            IsAdReward = session.IsAdReward
        };

        var entry = vhContext.Sessions.Attach(model);
        entry.Property(x => x.LastUsedTime).IsModified = true;
        entry.Property(x => x.EndTime).IsModified = true;
        entry.Property(x => x.SuppressedTo).IsModified = true;
        entry.Property(x => x.SuppressedBy).IsModified = true;
        entry.Property(x => x.ErrorMessage).IsModified = true;
        entry.Property(x => x.ErrorCode).IsModified = true;
        entry.Property(x => x.IsArchived).IsModified = true;
        entry.Property(x => x.AdExpirationTime).IsModified = true;
        entry.Property(x => x.IsAdReward).IsModified = true;
    }

    public void AccessUpdate(AccessCache access)
    {
        var model = new AccessModel {
            AccessId = access.AccessId,
            AccessTokenId = access.AccessTokenId,
            DeviceId = access.DeviceId,
            CreatedTime = access.CreatedTime,
            LastUsedTime = access.LastUsedTime,
            LastCycleReceivedTraffic = access.LastCycleReceivedTraffic,
            LastCycleSentTraffic = access.LastCycleSentTraffic,
            LastCycleTraffic = access.LastCycleTraffic,
            TotalReceivedTraffic = access.TotalReceivedTraffic,
            TotalSentTraffic = access.TotalSentTraffic,
            TotalTraffic = access.TotalTraffic,
            CycleTraffic = access.CycleTraffic,
            Description = access.Description
        };

        var entry = vhContext.Accesses.Attach(model);
        entry.Property(x => x.LastUsedTime).IsModified = true;
        entry.Property(x => x.TotalReceivedTraffic).IsModified = true;
        entry.Property(x => x.TotalSentTraffic).IsModified = true;
    }

    public async Task<SessionModel> SessionAdd(SessionCache session)
    {
        var entry = await vhContext.Sessions.AddAsync(session.ToModel());
        return entry.Entity;
    }

    public async Task AccessUsageAdd(AccessUsageModel[] sessionUsages)
    {
        await vhContext.AccessUsages.AddRangeAsync(sessionUsages);
    }

    public Task SaveChangesAsync()
    {
        return vhContext.SaveChangesAsync();
    }

    public Task<ServerModel> ServerGet(Guid projectId, Guid serverId,
        bool includeFarm = false, bool includeFarmProfile = false)
    {
        var query = vhContext.Servers
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => x.ServerId == serverId);

        if (includeFarm) query = query.Include(server => server.ServerFarm);
        if (includeFarmProfile) query = query.Include(server => server.ServerFarm!.ServerProfile);

        return query.SingleAsync();
    }

    public Task<ServerFarmCache> ServerFarmGet(Guid farmId)
    {
        return vhContext.ServerFarms
            .Where(farm => farm.ServerFarmId == farmId)
            .Select(farm => new ServerFarmCache {
                ProjectId = farm.ProjectId,
                ServerFarmId = farm.ServerFarmId,
                ServerFarmName = farm.ServerFarmName,
                TokenJson = farm.TokenJson,
                PushTokenToClient = farm.PushTokenToClient
            })
            .AsNoTracking()
            .SingleAsync();
    }

    public async Task<ServerFarmModel> ServerFarmGet(Guid serverFarmId, bool includeServersAndAccessPoints, 
        bool includeCertificates, bool includeServerProfile = true, bool includeTokenRepos = true)
    {
        var query = vhContext.ServerFarms
            .Where(farm => farm.Project!.DeletedTime == null)
            .Where(farm => farm.ServerFarmId == serverFarmId);

        if (includeTokenRepos)
            query = query.Include(x => x.TokenRepos);

        if (includeServerProfile)
            query = query.Include(x => x.ServerProfile);

        if (includeCertificates)
            query = query.Include(x => x.Certificates!.Where(y => !y.IsDeleted));

        if (includeServersAndAccessPoints)
            query = query
                .Include(farm => farm.Servers!.Where(server => !server.IsDeleted))
                .ThenInclude(server => server.AccessPoints)
                .Include(farm => farm.Servers!.Where(server => !server.IsDeleted))
                .ThenInclude(server => server.Location)
                .AsSingleQuery();

        var farmModel = await query.SingleAsync();
        return farmModel;
    }

    public ValueTask<ServerModel?> FindServerAsync(Guid serverServerId)
    {
        return vhContext.Servers.FindAsync(serverServerId);
    }

    public async Task AddAndSaveServerStatuses(ServerStatusBaseModel[] serverStatuses)
    {
        await using var transaction = vhContext.Database.CurrentTransaction == null
            ? await vhContext.Database.BeginTransactionAsync()
            : null;

        // remove old IsLast
        var serverIds = serverStatuses.Select(x => x.ServerId).Distinct();
        await vhContext.ServerStatuses
            .AsNoTracking()
            .Where(serverStatus => serverIds.Contains(serverStatus.ServerId))
            .ExecuteUpdateAsync(setPropertyCalls =>
                setPropertyCalls.SetProperty(serverStatus => serverStatus.IsLast, serverStatus => false));

        var models = serverStatuses.Select(x =>
            new ServerStatusModel {
                ServerStatusId = 0,
                IsLast = true,
                CreatedTime = x.CreatedTime,
                AvailableMemory = x.AvailableMemory,
                CpuUsage = x.CpuUsage,
                ServerId = x.ServerId,
                IsConfigure = x.IsConfigure,
                ProjectId = x.ProjectId,
                SessionCount = x.SessionCount,
                TcpConnectionCount = x.TcpConnectionCount,
                UdpConnectionCount = x.UdpConnectionCount,
                ThreadCount = x.ThreadCount,
                TunnelReceiveSpeed = x.TunnelReceiveSpeed,
                TunnelSendSpeed = x.TunnelSendSpeed
            });

        await vhContext.ServerStatuses.AddRangeAsync(models);
        await vhContext.SaveChangesAsync();

        if (transaction != null)
            await vhContext.Database.CommitTransactionAsync();
    }

    public Task<IpLockModel?> IpLockFind(Guid projectId, string clientIp)
    {
        return vhContext.IpLocks
            .Where(x => x.ProjectId == projectId)
            .Where(x => x.IpAddress == clientIp)
            .SingleOrDefaultAsync();
    }

    public Task<DeviceModel?> DeviceFind(Guid projectId, Guid clientId)
    {
        return vhContext.Devices
            .Where(x => x.ProjectId == projectId)
            .Where(x => x.ClientId == clientId)
            .SingleOrDefaultAsync();
    }

    public Task<LocationModel?> LocationFind(string countryCode, string? regionName, string? cityName)
    {
        return vhContext.Locations
            .Where(x => x.CountryCode == countryCode)
            .Where(x => x.RegionName == (regionName ?? "-"))
            .Where(x => x.CityName == (cityName ?? "-"))
            .SingleOrDefaultAsync();
    }

    public Task<AccessTokenModel> AccessTokenGet(Guid projectId, Guid accessTokenId, bool includeFarm = false)
    {
        var query = vhContext.AccessTokens
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => x.AccessTokenId == accessTokenId);

        if (includeFarm)
            query = query.Include(x => x.ServerFarm);

        return query.SingleAsync();
    }

    public async Task<LocationModel> LocationAdd(LocationModel location)
    {
        var entry = await vhContext.Locations.AddAsync(location);
        return entry.Entity;
    }

    public async Task<DeviceModel> DeviceAdd(DeviceModel device)
    {
        var entry = await vhContext.Devices.AddAsync(device);
        return entry.Entity;
    }

    public Task<FarmTokenRepoModel[]> FarmTokenRepoListPendingUpload()
    {
        return vhContext.FarmTokenRepos
            .Include(x=>x.ServerFarm)
            .Where(x => x.IsPendingUpload)
            .ToArrayAsync();
    }
}