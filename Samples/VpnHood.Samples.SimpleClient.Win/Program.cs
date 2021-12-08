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

            // a clientId should be generated for each client
            var clientId = Guid.Parse("7BD6C156-EEA3-43D5-90AF-B118FE47ED0A");

            // accessKey must obtain from the server
            var accessKey = "vh://eyJuYW1lIjoiVnBuSG9vZCBQdWJsaWMgU2VydmVycyIsInYiOjEsInNpZCI6MTAwMSwidGlkIjoiNWFhY2VjNTUtNWNhYy00NTdhLWFjYWQtMzk3Njk2OTIzNmY4Iiwic2VjIjoiNXcraUhNZXcwQTAzZ3c0blNnRFAwZz09IiwiaXN2IjpmYWxzZSwiaG5hbWUiOiJtby5naXdvd3l2eS5uZXQiLCJocG9ydCI6NDQzLCJjaCI6IjNnWE9IZTVlY3VpQzlxK3NiTzdobExva1FiQT0iLCJwYiI6dHJ1ZSwidXJsIjoiaHR0cHM6Ly93d3cuZHJvcGJveC5jb20vcy82YWlrdHFmM2xhZW9vaGY/ZGw9MSIsImVwIjpbIjUxLjgxLjIxMC4xNjQ6NDQzIl19";
            var token = Token.FromAccessKey(accessKey);

            var packetCapture = new WinDivertPacketCapture();
            var vpnHoodClient = new VpnHoodClient(packetCapture, clientId, token, new ClientOptions());

            // connect to VpnHood server
            vpnHoodClient.Connect().Wait();

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\nIP logging is enabled on these servers. Please follow United States law, especially if using torrent. Read privacy policy before use: https://github.com/vpnhood/VpnHood/blob/main/PRIVACY.md\n");
            Console.ResetColor();

            Console.WriteLine("VpnHood Client Is Running! Open your browser and browse the Internet! Press Ctrl+C to stop.");
            while (vpnHoodClient.State != ClientState.Disposed)
                Thread.Sleep(1000);
        }
    }
}