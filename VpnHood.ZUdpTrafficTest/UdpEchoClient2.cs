using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace VpnHood.ZUdpTrafficTest;

public class UdpEchoClient2
{
    private readonly UdpClient _udpClient = new(AddressFamily.InterNetwork);

    public UdpEchoClient2()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            _udpClient.Client.IOControl(-1744830452, new byte[] { 0 }, new byte[] { 0 });
    }


    public async Task StartAsync(IPEndPoint serverEp, int echoCount, int bufferSize, int timeout = 3000)
    {
        var task1 = StartAsync2(serverEp, echoCount, bufferSize, timeout);
        var task2 = ReceiveAsync();
        await Task.WhenAll(task1, task2);
    }

    private async Task StartAsync2(IPEndPoint serverEp, int echoCount, int bufferSize, int timeout = 3000)
    {
        Console.WriteLine($"Sending udp packets to {serverEp}... EchoCount: {echoCount}, BufferSize: {bufferSize}, Timeout: {timeout}");

        bufferSize += 8;
        var buffer = new byte[bufferSize];
        new Random().NextBytes(buffer);

        while (true)
        {
            for (var i = 0; i<3000 ; i++)
            {
                //send buffer
                Array.Copy(BitConverter.GetBytes(i), 0, buffer, 0, 4);
                Array.Copy(BitConverter.GetBytes(1), 0, buffer, 4, 4);
                Array.Copy(BitConverter.GetBytes(Environment.TickCount), 0, buffer, 8, 4);
                var res = await _udpClient.SendAsync(buffer, serverEp, CancellationToken.None);
                if (res != buffer.Length)
                {
                    Console.WriteLine("Could not send all data.");
                }

            }
            await Task.Delay(1000);
        }
        // ReSharper disable once FunctionNeverReturns
    }

    public async Task ReceiveAsync()
    {
        // wait for buffer
        var maxCount = 1000;
        while (true)
        {
            var delaySum = 0;
            for( var i=0; i<maxCount ; i++)
            {
                var udpResult = await _udpClient.ReceiveAsync();
                var resBuffer = udpResult.Buffer;
                var tickCount = BitConverter.ToInt32(resBuffer, 8);
                delaySum += Environment.TickCount - tickCount;
            }
            Console.Write($"{delaySum/maxCount}  ");
        }
        // ReSharper disable once FunctionNeverReturns
    }
}