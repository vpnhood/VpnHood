using System;
using System.Threading;
using VpnHood.Client;
using VpnHood.Client.Device.WinDivert;
using VpnHood.Common;
// ReSharper disable StringLiteralTypo

namespace VpnHood.Samples.SimpleClient.Win
{
    internal class Program
    {
        private static void Main()
        {
            Console.WriteLine("Hello VpnClient!");

            // clientId should be generated for each client

            // accessKey must obtain from the server
            var clientId = Guid.Parse("7BD6C156-EEA3-43D5-90AF-B118FE47ED0A");
            var accessKey =
                "vh://eyJuYW1lIjoiVnBuSG9vZCBQdWJsaWMgU2VydmVycyIsInYiOjEsInNpZCI6MTAwMSwidGlkIjoiNWFhY2VjNTUtNWNhYy00NTdhLWFjYWQtMzk3Njk2OTIzNmY4Iiwic2VjIjoiNXcraUhNZXcwQTAzZ3c0blNnRFAwZz09IiwiaXN2IjpmYWxzZSwiaG5hbWUiOiJtby5naXdvd3l2eS5uZXQiLCJocG9ydCI6NDQzLCJoZXAiOiI1MS44MS4yMTAuMTY0OjQ0MyIsImNoIjoiM2dYT0hlNWVjdWlDOXErc2JPN2hsTG9rUWJBPSIsInBiIjp0cnVlLCJ1cmwiOiJodHRwczovL3d3dy5kcm9wYm94LmNvbS9zLzExN2x6bHg2Z2N2YzNyZj9kbD0xIn0=";
            var token = Token.FromAccessKey(accessKey);

            var packetCapture = new WinDivertPacketCapture();
            var vpnHoodClient = new VpnHoodClient(packetCapture, clientId, token, new ClientOptions());

            vpnHoodClient.Connect().Wait();
            Console.WriteLine(
                "VpnHood Client Is Running! Open your browser and browse the Internet! Press Ctrl+C to stop.");
            while (vpnHoodClient.State != ClientState.Disposed)
                Thread.Sleep(1000);
        }
    }
}