using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Threading;
using VpnHood.Client;
using VpnHood.Client.Device.WinDivert;
using VpnHood.Common;
using VpnHood.Logging;

namespace VpnHood.Samples.SimpleClient.Win
{
    class Program
    {
        static void Main()
        {
            Console.WriteLine("Hello VpnClient!");

            // clientId should be generated for each client
            var clientId = Guid.Parse("7BD6C156-EEA3-43D5-90AF-B118FE47ED0A");

            // accessKey must obtain from the server
            var accessKey = "eyJuYW1lIjoiUHVibGljIFNlcnZlciIsInYiOjEsInNpZCI6NCwidGlkIjoiMmMwMmFjNDEtMDQwZi00NTc2LWI4Y2MtZGNmZTViOTE3MGI3Iiwic2VjIjoid3hWeVZvbjkxME9iYURDNW9BenpCUT09IiwiZG5zIjoiYXp0cm8uc2lnbWFsaWIub3JnIiwiaXN2ZG5zIjpmYWxzZSwicGtoIjoiUjBiaEsyNyt4dEtBeHBzaGFKbGk4dz09IiwiZXAiOlsiNTEuODEuODQuMTQyOjQ0MyJdLCJwYiI6dHJ1ZSwidXJsIjoiaHR0cHM6Ly93d3cuZHJvcGJveC5jb20vcy9obWhjaDZiMDl4N2Z1eDMvcHVibGljLmFjY2Vzc2tleT9kbD0xIn0=";
            var token = Token.FromAccessKey(accessKey);

            var packetCapture = new WinDivertPacketCapture();
            var vpnHoodClient = new VpnHoodClient(packetCapture, clientId, token, new ClientOptions() {});

            vpnHoodClient.Connect().Wait();
            Console.WriteLine("VpnHood Client Is Running! Open your browser and browse the Internet! Press Ctrl+C to stop.");
            while (vpnHoodClient.State == ClientState.Disposed)
                Thread.Sleep(1000);
        }
    }
}
