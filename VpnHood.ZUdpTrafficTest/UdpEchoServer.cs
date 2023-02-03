using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace VpnHood.ZUdpTrafficTest;

public class UdpEchoServer
{
    private readonly UdpClient _udpClient;
    private readonly Speedometer _sendSpeedometer = new("Sender");
    private readonly Speedometer _receivedSpeedometer = new("Receiver");

    public UdpEchoServer(IPEndPoint? serverEp = null)
    {
        serverEp ??= new IPEndPoint(IPAddress.Loopback, 59090);
        _udpClient = new UdpClient(serverEp);
       
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            _udpClient.Client.IOControl(-1744830452, new byte[] { 0 }, new byte[] { 0 });

    }

    public IPEndPoint? LocalEndPoint => (IPEndPoint?)_udpClient.Client.LocalEndPoint;

    public async Task StartAsync()
    {
        Console.WriteLine($"Starting server on {_udpClient.Client.LocalEndPoint}. Waiting for packet...");

        while (true)
        {
            // receiving
            var udpResult = await _udpClient.ReceiveAsync();
            var buffer = udpResult.Buffer;
            if (buffer.Length < 8)
            {
                _receivedSpeedometer.AddFailed();
                continue;
            }

            _receivedSpeedometer.AddSucceeded(buffer);

            // saving
            var echoCount = BitConverter.ToInt32(buffer, 4);
            for (var i = 0; i < echoCount; i++)
            {
                await _udpClient.SendAsync(buffer, buffer.Length, udpResult.RemoteEndPoint);
                _sendSpeedometer.AddSucceeded(buffer);
            }
        }
    }

    public void Start()
    {
        while (true)
        {
            // receiving
            IPEndPoint? remoteEp = null;
            var buffer = _udpClient.Receive(ref remoteEp);
            if (buffer.Length < 8)
            {
                _receivedSpeedometer.AddFailed();
                continue;
            }

            // saving
            _receivedSpeedometer.AddSucceeded(buffer);
            var echoCount = BitConverter.ToInt32(buffer, 4);
            for (var i = 0; i < echoCount; i++)
            {
                _udpClient.Send(buffer);
                _sendSpeedometer.AddSucceeded(buffer);
            }
        }
    }
}