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
        public string OrderId { get; } = Guid.NewGuid().ToString();
        public string? Description { get; set; }
        public bool IsCompleted { get; set; }
        public string? ServerId { get; set; }
    }

    public async Task FinishOrders(TestApp testApp)
    {
        Order[]? orders;
        lock (Orders)
            orders= Orders.ToArray();

        // mark all orders as completed and add them to the ips
        foreach (var order in orders) {
            order.IsCompleted = true;
            HostIps.Add(new HostProviderIp {
                IpAddress = await testApp.NewIpV4(),
                Description = order.Description,
                ServerId = order.ServerId,
            });
        }
    }

    public async Task<string?> GetServerIdFromIp(IPAddress serverIp, TimeSpan timeout)
    {
        await Task.Delay(0);
        return HostIps.FirstOrDefault(x => x.IpAddress.Equals(serverIp))?.ServerId;
    }

    public Task<string> OrderNewIp(string serverId, string? description, TimeSpan timeout)
    {
        var order = new Order {
            Description = description,
            ServerId = serverId,
            IsCompleted = false
        };

        lock (Orders) 
            Orders.Add(order);

        return Task.FromResult(order.OrderId);
    }

    public Task ReleaseIp(IPAddress ipAddress, TimeSpan timeout)
    {
        throw new NotImplementedException();
    }

    public async Task<HostProviderIp[]> LisIps(TimeSpan timeout)
    {
        await Task.Delay(0);
        return HostIps.ToArray();
    }

    public void DefineIp(IPAddress[] ipAddresses)
    {
        var serverId = Guid.NewGuid().ToString();
        foreach (var ipAddress in ipAddresses) {
            HostIps.Add(new HostProviderIp {
                IpAddress = ipAddress,
                ServerId = serverId,
                Description = ""
            });
        }
    }
}