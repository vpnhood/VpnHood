using System.Collections.Concurrent;
using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.Agent.Services;

public class CacheRepo
{
    public ConcurrentDictionary<Guid, ProjectCache> Projects = new();
    public ConcurrentDictionary<Guid, ServerFarmCache> ServerFarms = new();
    public ConcurrentDictionary<Guid, ServerCache> Servers = new();
    public ConcurrentDictionary<long, SessionCache> Sessions = new();
    public ConcurrentDictionary<Guid, AccessCache> Accesses = new();
    public ConcurrentDictionary<long, AccessUsageModel> SessionUsages = new();
    public readonly ConcurrentDictionary<string, DateTime> Ads = new();
    public readonly ConcurrentDictionary<Guid, DateTime> AdRewardedAccesses = new();
    public DateTime LastSavedTime = DateTime.MinValue;
}