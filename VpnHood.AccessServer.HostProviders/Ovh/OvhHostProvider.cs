using System.Net;
using Microsoft.Extensions.Logging;
using Ovh.Api;
using VpnHood.AccessServer.Abstractions.Providers.Hosts;
using VpnHood.AccessServer.HostProviders.Ovh.Dto;

namespace VpnHood.AccessServer.HostProviders.Ovh;

public class OvhHostProvider(
    ILogger logger,
    OvhHostProviderSettings settings
    ) : IHostProvider
{
    private readonly string _countryCode = settings.OvhSubsidiary;
    internal Client OvhClient { get; } = new(settings.EndPoint, settings.ApplicationKey, settings.ApplicationSecret, settings.ConsumerKey);

    private async Task<CartData> CreateNewCart(TimeSpan timeout)
    {
        var requestBody = new RequestBodyForCreateCart
        {
            description = "Created by API",
            expire = DateTime.UtcNow.AddHours(1),
            ovhSubsidiary = _countryCode
        };

        var cartData = await OvhClient.PostAsync<CartData>("/order/cart", requestBody, timeout: timeout);
        logger.LogInformation("New cart has been created. CartId: {CartId}", cartData.CartId);
        return cartData;
    }

    private async Task<CartItemId> AddNewIpOrderToCart(string cartId, TimeSpan timeout)
    {
        // ReSharper disable once StringLiteralTypo
        var requestBody = new IpOrderRequest
        {
            duration = "P1M",
            planCode = "ip-failover-arin",
            pricingMode = "default",
            quantity = 1,
        };
        var cartItemId = await OvhClient.PostAsync<CartItemId>($"/order/cart/{cartId}/ip", requestBody, timeout: timeout);
        logger.LogInformation("New IP order has been added to cart. Item: {CartItemId}", cartItemId.ItemId);
        return cartItemId;
    }

    // ReSharper disable once UnusedMethodReturnValue.Local
    private async Task<IpConfiguration> AddConfigurationToIp(string cartId, int itemId, string configLabel,
        string configValue, TimeSpan timeout)
    {
        var requestBody = new IpConfigRequest
        {
            label = configLabel,
            value = configValue
        };

        var ipConfiguration = await OvhClient.PostAsync<IpConfiguration>($"/order/cart/{cartId}/item/{itemId}/configuration",
            requestBody, timeout: timeout);

        logger.LogTrace("Config added to the ip order. Id: {Id}, Label: {Label}, Value: {Value}",
            ipConfiguration.Id, ipConfiguration.Label, ipConfiguration.Value);

        return ipConfiguration;
    }

    private async Task<string> Checkout(string cartId, TimeSpan timeout)
    {
        var requestBody = new CheckoutRequest
        {
            autoPayWithPreferredPaymentMethod = true,
            waiveRetractationPeriod = true
        };

        var checkoutData = await OvhClient.PostAsync<CheckoutData>($"/order/cart/{cartId}/checkout", requestBody, timeout: timeout);
        if (checkoutData?.OrderId == null)
            throw new Exception("The order id is null, maybe order is not successful please check your OVH account.");

        return checkoutData.OrderId.Value.ToString();
    }

    public async Task<string> OrderNewIp(string serverId, string? description, TimeSpan timeout)
    {
        // Create a new cart
        var cartData = await CreateNewCart(timeout);

        // Add new ip order to the cart
        var cartItemId = await AddNewIpOrderToCart(cartData.CartId, timeout);

        // Add required country config to the ip
        await AddConfigurationToIp(cartId: cartData.CartId, itemId: cartItemId.ItemId,
            configLabel: "country", _countryCode, timeout: timeout);

        // Add optional destination(VPS) config to the ip
        await AddConfigurationToIp(cartId: cartData.CartId, itemId: cartItemId.ItemId,
            configLabel: "destination", configValue: serverId, timeout: timeout);

        // Add optional description config to the ip
        await AddConfigurationToIp(cartId: cartData.CartId, itemId: cartItemId.ItemId,
            configLabel: "description", configValue: "Created by API", timeout: timeout);

        // Assign cart to current credential (User)
        var res = await OvhClient.PostAsync($"/order/cart/{cartData.CartId}/assign", timeout: timeout);

        // Finalize cart and checkout order
        var orderId = "";//await Checkout(cartData.CartId, timeout);
        return orderId;
    }

    public async Task<string?> GetServerIdFromIp(IPAddress serverIp, TimeSpan timeout)
    {
        var ipData = await OvhClient.GetAsync<IpData>($"/ip/{serverIp}", timeout: timeout);
        return ipData.RoutedTo.ServiceName;
    }

    public async Task ReleaseIp(IPAddress ipAddress, TimeSpan timeout)
    {
        var ipData = await OvhClient.GetAsync<IpData>($"/ip/{ipAddress}", timeout: timeout);
        if (!ipData.CanBeTerminated)
            throw new InvalidOperationException("The provided IP can not be terminated.");

        await OvhClient.PostAsync($"/ip/service/ip-{ipAddress}/terminate", timeout: timeout);
        logger.LogTrace("Request to termination IP completed successfully. You must confirm request manually after receive a confirmation email. Ip:{Ip}", ipAddress);
    }

    public async Task<IPAddress[]> ListIps(string? search, TimeSpan timeout)
    {
        var target = "/ip";
        if (!string.IsNullOrEmpty(search))
            target += $"?description=%25{search}%25";

        var ips = await OvhClient.GetAsync<string[]>(target, timeout: timeout);
        return ips.Select(x => IPAddress.Parse(x.Split('/')[0])).ToArray();
    }

    public async Task<HostProviderIp> GetIp(IPAddress ip, TimeSpan timeout)
    {
        var ipData = await OvhClient.GetAsync<IpData>($"/ip/{ip}", timeout: timeout);
        var hostProviderIp = new HostProviderIp
        {
            IpAddress = IPAddress.Parse(ipData.Ip.Split('/')[0]),
            ServerId = ipData.RoutedTo.ServiceName,
            Description = ipData.Description
        };
        return hostProviderIp;
    }

}