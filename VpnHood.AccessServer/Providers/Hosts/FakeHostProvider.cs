using System.Collections.Concurrent;
using System.Net;
using VpnHood.AccessServer.Abstractions.Providers.Hosts;
using VpnHood.Common.Net;

namespace VpnHood.AccessServer.Providers.Hosts;

public class FakeHostProvider(string providerName,
    FakeHostProvider.Settings providerSettings)
    : IHostProvider
{
    public static string BaseProviderName => "fake.internal";

    public class Settings
    {
        public TimeSpan? AutoCompleteDelay { get; init; } = TimeSpan.FromSeconds(15);
    }

    public string ProviderName => providerName;
    public ConcurrentDictionary<string, Order> Orders { get; } = [];
    public ConcurrentDictionary<IPAddress, HostProviderIp> HostIps { get; } = [];

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
        public IPAddress? ReleaseIp { get; set; }
    }

    private IPAddress BuildRandomIpAddress()
    {
        return new IPAddress(new byte[] {
            (byte) new Random().Next(128, 255),
            (byte) new Random().Next(0, 255),
            (byte) new Random().Next(0, 255),
            (byte) new Random().Next(1, 255)
        });
    }

    public async Task CompleteOrders(TimeSpan? delay = null)
    {
        delay ??= TimeSpan.Zero;
        await Task.Delay(delay.Value);

        // mark all orders as completed and add them to the ips
        foreach (var order in Orders.Values.Where(x => x is { IsCompleted: false, Type: Order.OrderType.NewIp })) {
            order.IsCompleted = true;
            var ipAddress = BuildRandomIpAddress();
            if (!HostIps.TryAdd(ipAddress, new HostProviderIp {
                IpAddress = ipAddress,
                Description = order.Description,
                ServerId = order.ServerId
            }))
                throw new Exception("Ip already exists.");
        }

        // mark all release orders as completed and remove them from the ips
        foreach (var order in Orders.Values.Where(x => x is { IsCompleted: false, Type: Order.OrderType.ReleaseIp })) {
            order.IsCompleted = true;
            HostIps.TryRemove(order.ReleaseIp!, out _);
        }

    }

    public async Task<string?> GetServerIdFromIp(IPAddress serverIp, TimeSpan timeout)
    {
        await Task.Delay(0);

        var bytes = serverIp.GetAddressBytes();
        return serverIp.IsV6() && bytes[0] == 255 && bytes[0] == 255
            ? serverIp.ToString()
            : HostIps.FirstOrDefault(x => x.Value.IpAddress.Equals(serverIp)).Value.ServerId;
    }

    public async Task<string> OrderNewIp(string serverId, string? description, TimeSpan timeout)
    {
        await Task.Delay(0);

        var order = new Order {
            Type = Order.OrderType.NewIp,
            Description = description,
            ServerId = serverId,
            IsCompleted = false
        };

        Orders.TryAdd(order.OrderId, order);

        if (providerSettings.AutoCompleteDelay != null)
            _ = CompleteOrders(providerSettings.AutoCompleteDelay.Value);

        return order.OrderId;
    }

    public async Task ReleaseIp(IPAddress ipAddress, TimeSpan timeout)
    {
        await Task.Delay(0);
        var order = new Order {
            Type = Order.OrderType.ReleaseIp,
            IsCompleted = false,
            ReleaseIp = ipAddress
        };

        Orders.TryAdd(order.OrderId, order);

        if (providerSettings.AutoCompleteDelay != null)
            _ = CompleteOrders(providerSettings.AutoCompleteDelay.Value);
    }

    public async Task<HostProviderIp> GetIp(IPAddress ipAddress, TimeSpan timeout)
    {
        await Task.Delay(0);
        return HostIps[ipAddress];
    }

    public async Task<IPAddress[]> ListIps(string? search, TimeSpan timeout)
    {
        await Task.Delay(0);
        return HostIps.Values
            .Where(x =>
                string.IsNullOrEmpty(search) ||
                x.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) == true)
            .Select(x => x.IpAddress)
            .ToArray();
    }
}
