using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace VpnHood.ZUdpTrafficTest;

public class UdpEchoClient
{
    private readonly UdpClient _udpClient = new(AddressFamily.InterNetwork);
    private readonly Speedometer _sendSpeedometer = new("Sender");
    private readonly Speedometer _receivedSpeedometer = new("Receiver");

    public UdpEchoClient()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            _udpClient.Client.IOControl(-1744830452, new byte[] { 0 }, new byte[] { 0 });
    }

    private static bool CompareBuffer(byte[] buffer1, byte[] buffer2, int length)
    {
        for (var i = 0; i < length; i++)
            if (buffer1[i] != buffer2[i])
                return false;

        return false;
    }

    public async Task StartAsync(IPEndPoint serverEp, int echoCount, int bufferSize, int timeout = 3000)
    {
        Console.WriteLine($"Sending udp packets to {serverEp}... EchoCount: {echoCount}, BufferSize: {bufferSize}, Timeout: {timeout}");

        bufferSize += 8;
        var buffer = new byte[bufferSize];
        new Random().NextBytes(buffer);

        for (var i = 0; ; i++)
        {
            //send buffer
            Array.Copy(BitConverter.GetBytes(i), 0, buffer, 0, 4);
            Array.Copy(BitConverter.GetBytes(echoCount), 0, buffer, 4, 4);
            var res = await _udpClient.SendAsync(buffer, serverEp, CancellationToken.None);
            if (res!= buffer.Length)
            {
                _sendSpeedometer.AddFailed();
                Console.WriteLine("Could not send all data.");
            }
            _sendSpeedometer.AddSucceeded(buffer);

            // wait for buffer
            for (var j = 0; j < echoCount; j++)
            {
                try
                {
                    using var cancellationTokenSource = new CancellationTokenSource(timeout);
                    var udpResult = await _udpClient.ReceiveAsync(cancellationTokenSource.Token);
                    var resBuffer = udpResult.Buffer;
                    var packetNumber = BitConverter.ToInt32(buffer, 0);
                    if (packetNumber != i || resBuffer.Length != buffer.Length || CompareBuffer(buffer, resBuffer, 8))
                    {
                        j--;
                        Console.WriteLine("Invalid data received");
                        continue;
                    }

                    _receivedSpeedometer.AddSucceeded(buffer);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("A packet loss!");
                    _receivedSpeedometer.AddFailed();
                    break;
                }
            }
        }
    }

}