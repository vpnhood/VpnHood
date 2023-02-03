using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace VpnHood.ZUdpTrafficTest;

public class UdpEchoClient2
{
    private readonly UdpClient _udpClient = new(AddressFamily.InterNetwork);
    //private readonly Speedometer _sendSpeedometer = new("Sender");
    //private readonly Speedometer _receivedSpeedometer = new("Receiver");

    public UdpEchoClient2()
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
        var task1 = StartAsync2(serverEp, echoCount, bufferSize, timeout);
        var task2 = ReceiveAsync(serverEp, echoCount, bufferSize, timeout);
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
    }

    public async Task ReceiveAsync(IPEndPoint serverEp, int echoCount, int bufferSize, int timeout = 3000)
    {
        // wait for buffer
        int max_count = 1000;
        while (true)
        {
            int delay_sum = 0;
            for( int i=0; i<max_count ; i++)
            {
                var udpResult = await _udpClient.ReceiveAsync();
                var resBuffer = udpResult.Buffer;
                var tickCount = BitConverter.ToInt32(resBuffer, 8);
                delay_sum += Environment.TickCount - tickCount;
            }
            Console.Write($"{delay_sum/max_count}  ");
        }
    }
}