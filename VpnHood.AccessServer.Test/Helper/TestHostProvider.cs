using System.Net;
using VpnHood.AccessServer.Abstractions.Providers.Hosts;

namespace VpnHood.AccessServer.Test.Helper;

public class TestHostProvider(string providerName) : IHostProvider
{
    public string ProviderName => providerName;
    public List<Order> Orders { get; } = [];
    public List<HostProviderIp> HostIps { get; } = [];

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
            lock (HostIps)
                HostIps.Add(new HostProviderIp {
                    IpAddress = ipAddress,
                    Description = order.Description,
                    ServerId = order.ServerId,
                });
        }

        foreach (var order in orders.Where(x => x is { IsCompleted: false, Type: Order.OrderType.ReleaseIp })) {
            order.IsCompleted = true;
            lock (HostIps)
                HostIps.RemoveAll(x => x.IpAddress.Equals(order.ReleaseIp));
        }

    }

    public async Task<string?> GetServerIdFromIp(IPAddress serverIp, TimeSpan timeout)
    {
        await Task.Delay(0);
        lock (HostIps)
            return HostIps.FirstOrDefault(x => x.IpAddress.Equals(serverIp))?.ServerId;
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

    public async Task<HostProviderIp[]> LisIps(TimeSpan timeout)
    {
        await Task.Delay(0);
        lock (HostIps)
            return HostIps.ToArray();
    }

    public void DefineIp(IPAddress[] ipAddresses)
    {
        var serverId = Guid.NewGuid().ToString();
        foreach (var ipAddress in ipAddresses) {
            lock (HostIps)
                HostIps.Add(new HostProviderIp {
                    IpAddress = ipAddress,
                    ServerId = serverId,
                    Description = ""
                });
        }
    }
}