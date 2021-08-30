using System;
using System.Threading;
using VpnHood.Client;
using VpnHood.Client.Device.WinDivert;
using VpnHood.Common;

namespace VpnHood.Samples.SimpleClient.Win
{
    class Program
    {
        static void Main()
        {
            Console.WriteLine("Hello VpnClient!");

            // clientId should be generated for each client

            // accessKey must obtain from the server
            var clientId = Guid.Parse("7BD6C156-EEA3-43D5-90AF-B118FE47ED0A");
            var accessKey = "vh://eyJuYW1lIjoiUHVibGljIFNlcnZlciIsInYiOjEsInNpZCI6MTEsInRpZCI6IjEwNDczNTljLWExMDctNGU0OS04NDI1LWMwMDRjNDFmZmI4ZiIsInNlYyI6IlRmK1BpUTRaS1oyYW1WcXFPNFpzdGc9PSIsImRucyI6Im1vLmdpd293eXZ5Lm5ldCIsImlzdmRucyI6ZmFsc2UsInBraCI6Ik1Da3lsdTg0N2J5U0Q4bEJZWFczZVE9PSIsImNoIjoiM2dYT0hlNWVjdWlDOXErc2JPN2hsTG9rUWJBPSIsImVwIjpbIjUxLjgxLjgxLjI1MDo0NDMiXSwicGIiOnRydWUsInVybCI6Imh0dHBzOi8vd3d3LmRyb3Bib3guY29tL3MvaG1oY2g2YjA5eDdmdXgzL3B1YmxpYy5hY2Nlc3NrZXk/ZGw9MSJ9";
            var token = Token.FromAccessKey(accessKey);

            var packetCapture = new WinDivertPacketCapture();
            var vpnHoodClient = new VpnHoodClient(packetCapture, clientId, token, new ClientOptions());

            vpnHoodClient.Connect().Wait();
            Console.WriteLine("VpnHood Client Is Running! Open your browser and browse the Internet! Press Ctrl+C to stop.");
            while (vpnHoodClient.State != ClientState.Disposed)
                Thread.Sleep(1000);
        }
    }
}
