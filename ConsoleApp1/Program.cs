﻿using System.Net;
using VpnHood.AccessServer.HostProviders.Ovh;
using VpnHood.AccessServer.HostProviders.Ovh.Dto;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;

namespace ConsoleApp1;

internal class Program
{
    private static string BuildProjectTag(Guid projectId) => $"#project:{projectId}";

    static async Task Main(string[] args)
    {
        await Task.CompletedTask;
        //var logger = VhLogger.CreateConsoleLogger();
        //var hostProvider = new OvhHostProvider(logger, new OvhHostProviderSettings {
        //});
        
        //var providerServerId = await hostProvider.GetServerIdFromIp(IPAddress.Parse("15.204.131.99"), TimeSpan.FromMinutes(5)) ?? "";
        //var ip = await hostProvider.GetIp(IPAddress.Parse("15.204.131.99"), TimeSpan.FromMinutes(1));
        //await hostProvider.UpdateIpDesc(IPAddress.Parse("15.204.131.99"), null, TimeSpan.FromMinutes(1));
        //var ip = await hostProvider.GetIp(IPAddress.Parse("15.204.131.99"), TimeSpan.FromMinutes(1));

        //vpn.friend #project:8b90f69b-264f-4d4f-9d42-f614de4e3aea



        //try {
        //    //var z = await hostProvider.OrderNewIp(providerServerId, BuildProjectTag(Guid.Parse("8b90f69b-264f-4d4f-9d42-f614de4e3aea")), TimeSpan.FromMinutes(10));
        //    Console.WriteLine("Completed");

        //}
        //catch (Exception ex) {
        //    Console.WriteLine(ex.ToString());
        //}

    }
}
