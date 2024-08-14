using System.Collections.Concurrent;
using System.Net;
using VpnHood.AccessServer.Abstractions.Providers.Hosts;

namespace VpnHood.AccessServer.Test.Helper;

public class TestHostProvider(string providerName) : IHostProvider
{
    public string ProviderName => providerName;
    public List<Order> Orders { get; } = [];
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

    public async Task CompleteOrders(TestApp testApp)
    {
        Order[]? orders;
        lock (Orders)
            orders = Orders.ToArray();

        // mark all orders as completed and add them to the ips
        foreach (var order in orders.Where(x => x is { IsCompleted: false, Type: Order.OrderType.NewIp })) {
            order.IsCompleted = true;
            var ipAddress = await testApp.NewIpV4();
            if (!HostIps.TryAdd(ipAddress, new HostProviderIp {
                IpAddress = ipAddress,
                Description = order.Description,
                ServerId = order.ServerId
            }))
                throw new Exception("Ip already exists.");
        }

        // mark all release orders as completed and remove them from the ips
        foreach (var order in orders.Where(x => x is { IsCompleted: false, Type: Order.OrderType.ReleaseIp })) {
            order.IsCompleted = true;
            lock (HostIps)
                HostIps.TryRemove(order.ReleaseIp!, out _);
        }

    }

    public async Task<string?> GetServerIdFromIp(IPAddress serverIp, TimeSpan timeout)
    {
        await Task.Delay(0);
        lock (HostIps)
            return HostIps.FirstOrDefault(x => x.Value.IpAddress.Equals(serverIp)).Value.ServerId;
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

        lock (Orders)
            Orders.Add(order);

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

        lock (Orders)
            Orders.Add(order);
    }

    public async Task<HostProviderIp> GetIp(IPAddress ipAddress, TimeSpan timeout)
    {
        await Task.Delay(0);
        return HostIps[ipAddress];
    }

    public async Task<string[]> ListIps(string? search, TimeSpan timeout)
    {
        await Task.Delay(0);
        return HostIps.Values
            .Where(x =>
                string.IsNullOrEmpty(search) ||
                x.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) == true)
            .Select(x => x.IpAddress.ToString())
            .ToArray();
    }

    public void DefineIp(IPAddress[] ipAddresses)
    {
        var serverId = Guid.NewGuid().ToString();
        foreach (var ipAddress in ipAddresses) {
            var item = new HostProviderIp {
                IpAddress = ipAddress,
                ServerId = serverId,
                Description = ""
            };

            if (!HostIps.TryAdd(ipAddress, item))
                throw new Exception("Ip could not be added.");
        }
    }
}