using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrayMint.Common.Utils;
using VpnHood.AccessServer.Abstractions.Providers.Hosts;
using VpnHood.AccessServer.Persistence.Models.HostOrders;
using VpnHood.AccessServer.Repos;
using VpnHood.Common.Converters;
using VpnHood.Common.Net;

namespace VpnHood.AccessServer.Providers.Hosts;

public class FakeHostProvider : IHostProvider
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<FakeHostProvider> _logger;
    private readonly Guid _hostProviderId;
    private readonly Guid _projectId;
    private readonly Settings _providerSettings;

    public static string BaseProviderName => "fake.internal";
    public string ProviderName { get; }

    private async Task<FakeDb> GetFakeDb(VhRepo vhRepo)
    {
        var hostProvider = await vhRepo.HostProviderGet(_projectId, _hostProviderId, asNoTracking: true);
        return GmUtil.JsonDeserialize<FakeDb>(hostProvider.CustomData ?? "{}");
    }

    private FakeHostProvider(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<FakeHostProvider> logger,
        HostProviderModel hostProviderModel)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _hostProviderId = hostProviderModel.HostProviderId;
        _logger = logger;
        _projectId = hostProviderModel.ProjectId;
        _providerSettings = GmUtil.JsonDeserialize<Settings>(string.IsNullOrEmpty(hostProviderModel.Settings) ? "{}" : hostProviderModel.Settings);
        ProviderName = hostProviderModel.HostProviderName;
    }

    public static async Task<FakeHostProvider> Create(
        IServiceScopeFactory serviceScopeFactory,
        Guid hostProviderId)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var vhRepo = scope.ServiceProvider.GetRequiredService<VhRepo>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<FakeHostProvider>>();
        var hostProviderModel = await vhRepo.HostProviderGet(hostProviderId: hostProviderId);
        var provider = new FakeHostProvider(serviceScopeFactory, logger, hostProviderModel);
        return provider;
    }


    public class Settings
    {
        public TimeSpan? AutoCompleteDelay { get; init; } = TimeSpan.FromSeconds(15);
    }

    private async Task Save(VhRepo vhRepo, FakeDb fakeDb)
    {
        var hostProvider = await vhRepo.HostProviderGet(_projectId, _hostProviderId);
        hostProvider.CustomData = JsonSerializer.Serialize(fakeDb);
        await vhRepo.SaveChangesAsync();
    }


    // ReSharper disable once ClassNeverInstantiated.Local
    private class FakeDb
    {
        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Local
        public ConcurrentDictionary<string, Order> Orders { get; init; } = [];

        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Local
        public ConcurrentDictionary<string, ProviderHostIp> HostIps { get; init; } = [];
    }

    public class Order
    {
        public enum OrderType
        {
            NewIp,
            ReleaseIp
        }

        public required OrderType Type { get; set; }
        public string OrderId { get; } = Guid.NewGuid().ToString();
        public string? Description { get; set; }
        public bool IsCompleted { get; set; }
        public string? ServerId { get; set; }
    
        [JsonConverter(typeof(IPAddressConverter))]
        public IPAddress? ReleaseIp { get; set; }
    }

    private static IPAddress BuildRandomIpAddress()
    {
        return new IPAddress([
            (byte)Random.Shared.Next(128, 255),
            (byte)Random.Shared.Next(0, 255),
            (byte)Random.Shared.Next(0, 255),
            (byte)Random.Shared.Next(1, 255)
        ]);
    }

    public async Task CompleteOrders(TimeSpan delay)
    {
        await Task.Delay(delay);
        await CompleteOrders();
    }

    public async Task CompleteOrders()
    {
        using var asyncLock = await AsyncLock.LockAsync($"FakeHostProvider_{_hostProviderId}");
        
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var vhRepo = scope.ServiceProvider.GetRequiredService<VhRepo>();
        var fakeDb = await GetFakeDb(vhRepo);

        // mark all orders as completed and add them to the ips
        foreach (var order in fakeDb.Orders.Values.Where(x => x is { IsCompleted: false, Type: Order.OrderType.NewIp })) {
            order.IsCompleted = true;
            var ipAddress = BuildRandomIpAddress();
            if (fakeDb.HostIps.TryAdd(ipAddress.ToString(), new ProviderHostIp {
                IpAddress = ipAddress,
                Description = order.Description,
                ServerId = order.ServerId,
                IsAdditional = true
            })) {
                _logger.LogInformation("FakeProvider allocate an Ip. Ip: {Ip}", ipAddress);
            }
            else
                throw new Exception("Ip already exists.");
        }

        // mark all release orders as completed and remove them from the ips
        foreach (var order in fakeDb.Orders.Values.Where(x => x is { IsCompleted: false, Type: Order.OrderType.ReleaseIp })) {
            order.IsCompleted = true;
            if (fakeDb.HostIps.TryRemove(order.ReleaseIp!.ToString(), out _))
                _logger.LogInformation("FakeProvider released an Ip. Ip: {Ip}", order.ReleaseIp);
        }

        await Save(vhRepo, fakeDb);

    }

    public async Task<string?> GetServerIdFromIp(IPAddress serverIp, TimeSpan timeout)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var vhRepo = scope.ServiceProvider.GetRequiredService<VhRepo>();
        var fakeDb = await GetFakeDb(vhRepo);

        var bytes = serverIp.GetAddressBytes();
        return serverIp.IsV6() && bytes[0] == 255 && bytes[0] == 255
            ? serverIp.ToString()
            : fakeDb.HostIps.FirstOrDefault(x => x.Value.IpAddress.Equals(serverIp)).Value.ServerId;
    }

    //add HostIp directly to the fakeDb for test
    public async Task AddHostIp(ProviderHostIp providerHostIp)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var vhRepo = scope.ServiceProvider.GetRequiredService<VhRepo>();
        var fakeDb = await GetFakeDb(vhRepo);

        fakeDb.HostIps.TryAdd(providerHostIp.IpAddress.ToString(), providerHostIp);
        await Save(vhRepo, fakeDb);
    }


    public async Task<string> OrderNewIp(string serverId, TimeSpan timeout)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var vhRepo = scope.ServiceProvider.GetRequiredService<VhRepo>();
        var fakeDb = await GetFakeDb(vhRepo);

        var order = new Order {
            Type = Order.OrderType.NewIp,
            ServerId = serverId,
            IsCompleted = false
        };

        fakeDb.Orders.TryAdd(order.OrderId, order);
        await Save(vhRepo, fakeDb);

        if (_providerSettings.AutoCompleteDelay != null)
            _ = CompleteOrders(_providerSettings.AutoCompleteDelay.Value);

        return order.OrderId;
    }

    public async Task ReleaseIp(IPAddress ipAddress, TimeSpan timeout)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var vhRepo = scope.ServiceProvider.GetRequiredService<VhRepo>();
        var fakeDb = await GetFakeDb(vhRepo);

        // check is the ip is allocated
        if (!fakeDb.HostIps.ContainsKey(ipAddress.ToString()))
            throw new Exception("Ip is not allocated in this provider.");

        var order = new Order {
            Type = Order.OrderType.ReleaseIp,
            IsCompleted = false,
            ReleaseIp = ipAddress
        };

        fakeDb.Orders.TryAdd(order.OrderId, order);
        await Save(vhRepo, fakeDb);

        if (_providerSettings.AutoCompleteDelay != null)
            _ = CompleteOrders(_providerSettings.AutoCompleteDelay.Value);
    }

    public async Task<ProviderHostIp> GetIp(IPAddress ipAddress, TimeSpan timeout)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var vhRepo = scope.ServiceProvider.GetRequiredService<VhRepo>();
        var fakeDb = await GetFakeDb(vhRepo);

        return fakeDb.HostIps[ipAddress.ToString()];
    }

    public async Task UpdateIpDesc(IPAddress ipAddress, string? description, TimeSpan timeout)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var vhRepo = scope.ServiceProvider.GetRequiredService<VhRepo>();
        var fakeDb = await GetFakeDb(vhRepo);
        fakeDb.HostIps[ipAddress.ToString()].Description = description;
        await Save(vhRepo, fakeDb);
    }

    public async Task<IPAddress[]> ListIps(string? search, TimeSpan timeout)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var vhRepo = scope.ServiceProvider.GetRequiredService<VhRepo>();
        var fakeDb = await GetFakeDb(vhRepo);

        return fakeDb.HostIps.Values
            .Where(x =>
                string.IsNullOrEmpty(search) ||
                x.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) == true)
            .Select(x => x.IpAddress)
            .ToArray();
    }
}
