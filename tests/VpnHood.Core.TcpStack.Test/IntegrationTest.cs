using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.VpnAdapters.Abstractions;
using VpnHood.Core.VpnAdapters.WinDivert;

namespace VpnHood.Core.TcpStack.Test;

// WinDivert is a Windows-only kernel driver (loads kernel32.dll); these integration tests can only
// run on Windows. On other platforms MSTest skips them instead of failing on the missing native lib.
[TestClass]
[OSCondition(OperatingSystems.Windows)]
[DoNotParallelize] // uses the machine-wide WinDivert adapter
public sealed class TcpStackIntegrationTest
{
    private static readonly IPAddress TestServerIp = IPAddress.Parse("11.0.0.1");
    private const int TestServerPort = 8080;
    private const int TestDataSize = 20 * 1024 * 1024;  // 200 MB

    /// <summary>
    /// Diagnostic test with minimal data to understand WinDivert integration issues
    /// </summary>
    [TestMethod]
    [Timeout(60000)] // 60 seconds for debugging
    public async Task DiagnosticTest_SmallData_WinDivert()
    {
        var diagServerIp = IPAddress.Parse("11.0.0.2");
        const int diagServerPort = 8081;
        Console.WriteLine("=== DIAGNOSTIC TEST START ===");
        Console.WriteLine($"Test Server: {diagServerIp}:{diagServerPort}");

        var tcpStack = new LocalTcpStack();
        var adapterSettings = new WinDivertVpnAdapterSettings {
            AdapterName = "VpnHoodDiag",
            ExcludeLocalNetwork = false,
            SimulateDns = false,
            AutoDisposePackets = true,
            Blocking = true,
        };

        using var adapter = new WinDivertVpnAdapter(adapterSettings);

        var incomingPackets = new List<(DateTime Time, string Info, byte[] Data)>();
        var outgoingPackets = new List<(DateTime Time, string Info, byte[] Data)>();

        // Detailed packet logging
        adapter.PacketReceived += (_, packet) => {
            var time = DateTime.Now;
            var info = $"Proto={packet.Protocol}, Src={packet.SourceAddress}, Dst={packet.DestinationAddress}, Len={packet.Buffer.Length}";

            if (packet.Protocol == IpProtocol.Tcp) {
                var tcp = packet.ExtractTcp();
                info = $"TCP {packet.SourceAddress}:{tcp.SourcePort} -> {packet.DestinationAddress}:{tcp.DestinationPort} " +
                       $"[SYN={tcp.Synchronize}, ACK={tcp.Acknowledgment}, FIN={tcp.Finish}, RST={tcp.Reset}, PSH={tcp.Push}] " +
                       $"Seq={tcp.SequenceNumber}, AckNum={tcp.AcknowledgmentNumber}, PayloadLen={tcp.Payload.Length}";

                if (tcp.Payload.Length > 0) {
                    var payloadHex = BitConverter.ToString(tcp.Payload.Span.ToArray().Take(Math.Min(32, tcp.Payload.Length)).ToArray());
                    info += $", PayloadHex={payloadHex}";
                }
            }

            Console.WriteLine($"[{time:HH:mm:ss.fff}] <<< INCOMING: {info}");
            lock (incomingPackets) incomingPackets.Add((time, info, packet.Buffer.ToArray()));

            tcpStack.ProcessIncoming(packet);
        };

        tcpStack.OnPacketSend = packet => {
            var time = DateTime.Now;
            var info = $"Proto={packet.Protocol}, Src={packet.SourceAddress}, Dst={packet.DestinationAddress}";

            if (packet.Protocol == IpProtocol.Tcp) {
                var tcp = packet.ExtractTcp();
                info = $"TCP {packet.SourceAddress}:{tcp.SourcePort} -> {packet.DestinationAddress}:{tcp.DestinationPort} " +
                       $"[SYN={tcp.Synchronize}, ACK={tcp.Acknowledgment}, FIN={tcp.Finish}, RST={tcp.Reset}, PSH={tcp.Push}] " +
                       $"Seq={tcp.SequenceNumber}, AckNum={tcp.AcknowledgmentNumber}, PayloadLen={tcp.Payload.Length}";

                if (tcp.Payload.Length > 0) {
                    var payloadHex = BitConverter.ToString(tcp.Payload.Span.ToArray().Take(Math.Min(32, tcp.Payload.Length)).ToArray());
                    info += $", PayloadHex={payloadHex}";
                }
            }

            Console.WriteLine($"[{time:HH:mm:ss.fff}] >>> OUTGOING: {info}");
            lock (outgoingPackets) outgoingPackets.Add((time, info, packet.Buffer.ToArray()));

            // ReSharper disable once AccessToDisposedClosure
            adapter.SendPacketQueued(packet);
        };

        // Setup listener
        var listener = tcpStack.Listen(new IpEndPointValue(diagServerIp, diagServerPort));
        Console.WriteLine($"[SETUP] TCP Stack listening on {diagServerIp}:{diagServerPort}");

        // Simple echo server that logs everything
        var serverReceivedData = new List<byte>();
        _ = Task.Run(async () => {
            Console.WriteLine("[SERVER] Waiting for connection...");
            await foreach (var stream in listener.AcceptAllAsync()) {
                Console.WriteLine("[SERVER] ✅ Connection accepted!");
                try {
                    var buffer = new byte[1024];
                    while (true) {
                        Console.WriteLine("[SERVER] Waiting for data...");
                        var bytesRead = await stream.Stream.ReadAsync(buffer, 0, buffer.Length);
                        Console.WriteLine($"[SERVER] Read {bytesRead} bytes");

                        if (bytesRead == 0) {
                            Console.WriteLine("[SERVER] Connection closed by client");
                            break;
                        }

                        lock (serverReceivedData)
                            serverReceivedData.AddRange(buffer.Take(bytesRead));

                        var dataHex = BitConverter.ToString(buffer.Take(Math.Min(32, bytesRead)).ToArray());
                        Console.WriteLine($"[SERVER] Received data (hex): {dataHex}");

                        Console.WriteLine($"[SERVER] Echoing {bytesRead} bytes back...");
                        await stream.Stream.WriteAsync(buffer, 0, bytesRead);
                        Console.WriteLine("[SERVER] Echo complete");
                    }
                }
                catch (Exception ex) {
                    Console.WriteLine($"[SERVER] Error: {ex.Message}");
                }
                break;
            }
        });

        try {
            // Start adapter
            var options = new VpnAdapterOptions {
                SessionName = "DiagnosticTest",
                VirtualIpNetworkV4 = IpNetwork.Parse("10.0.0.0/24"),
                IncludeNetworks = [new IpNetwork(diagServerIp, 32)]
            };

            Console.WriteLine("[SETUP] Starting WinDivert adapter...");
            await adapter.Start(options, CancellationToken.None);
            Console.WriteLine("[SETUP] Adapter started");

            // Connect with TcpClient
            Console.WriteLine("[CLIENT] Creating TcpClient...");
            using var tcpClient = new TcpClient();
            tcpClient.NoDelay = true; // Disable Nagle's algorithm

            Console.WriteLine($"[CLIENT] Connecting to {diagServerIp}:{diagServerPort}...");
            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await tcpClient.ConnectAsync(diagServerIp, diagServerPort, connectCts.Token);
            Console.WriteLine("[CLIENT] Connected!");

            await using var stream = tcpClient.GetStream();

            // Send just 5 bytes: "HELLO"
            var testData = "HELLO"u8.ToArray();
            Console.WriteLine($"[CLIENT] Sending {testData.Length} bytes: {BitConverter.ToString(testData)}");
            await stream.WriteAsync(testData, connectCts.Token);
            await stream.FlushAsync(connectCts.Token);
            Console.WriteLine("[CLIENT] Data sent, waiting for echo...");

            // Wait for echo
            var receiveBuffer = new byte[100];
            using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var totalReceived = 0;
            while (totalReceived < testData.Length) {
                Console.WriteLine($"[CLIENT] Waiting to receive (got {totalReceived}/{testData.Length} so far)...");
                var bytesRead = await stream.ReadAsync(receiveBuffer.AsMemory(totalReceived), readCts.Token);
                Console.WriteLine($"[CLIENT] Received {bytesRead} bytes");

                if (bytesRead == 0) {
                    Console.WriteLine("[CLIENT] Connection closed");
                    break;
                }
                totalReceived += bytesRead;
            }

            Console.WriteLine($"[CLIENT] Total received: {totalReceived} bytes");
            Console.WriteLine($"[CLIENT] Received data: {BitConverter.ToString(receiveBuffer.Take(totalReceived).ToArray())}");

            // Summary
            Console.WriteLine("\n=== PACKET SUMMARY ===");
            Console.WriteLine($"Incoming packets: {incomingPackets.Count}");
            Console.WriteLine($"Outgoing packets: {outgoingPackets.Count}");
            Console.WriteLine($"Server received bytes: {serverReceivedData.Count}");

            // Assert
            Assert.AreEqual(testData.Length, totalReceived, "Should receive all echoed data");
            CollectionAssert.AreEqual(testData, receiveBuffer.Take(totalReceived).ToArray(), "Echoed data should match");

            Console.WriteLine("\n=== TEST PASSED ===");
        }
        catch (Exception ex) {
            Console.WriteLine("\n=== TEST FAILED ===");
            Console.WriteLine($"Exception: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"Stack: {ex.StackTrace}");

            Console.WriteLine("\n=== PACKET DUMP ===");
            Console.WriteLine("--- Incoming packets ---");
            foreach (var (time, info, _) in incomingPackets)
                Console.WriteLine($"  [{time:HH:mm:ss.fff}] {info}");

            Console.WriteLine("--- Outgoing packets ---");
            foreach (var (time, info, _) in outgoingPackets)
                Console.WriteLine($"  [{time:HH:mm:ss.fff}] {info}");

            throw;
        }
        finally {
            Console.WriteLine("[CLEANUP] Stopping adapter...");
            adapter.Stop();
        }
    }

    [TestMethod]
    [Timeout(180000)] // 3 minutes for 200 MB
    public async Task TestTcpStackWithWinDivertAdapter_Echo_ShouldSucceed()
    {
        // Arrange
        var testData = GenerateRandomTestData(TestDataSize);
        var tcpStack = new LocalTcpStack();
        var completionSource = new TaskCompletionSource<bool>();
        var receivedData = new List<byte>();

        var adapterSettings = new WinDivertVpnAdapterSettings {
            AdapterName = "VpnHoodTest",
            ExcludeLocalNetwork = false, // We want to capture local traffic for testing
            SimulateDns = false,
            // Required properties
            AutoDisposePackets = true,
            Blocking = true,
        };

        using var adapter = new WinDivertVpnAdapter(adapterSettings);

        var packetCount = 0;
        var tcpPacketCount = 0;

        // Setup TCP stack integration with adapter's PacketReceived event
        adapter.PacketReceived += (_, packet) => {
            try {
                if (packet.Protocol == IpProtocol.Tcp) tcpPacketCount++;
                packetCount++;
                // Process with TCP stack
                tcpStack.ProcessIncoming(packet);
            }
            catch (Exception ex) {
                Console.WriteLine($"[ADAPTER] Error processing packet: {ex.Message}");
            }
        };

        // Setup TCP stack to send packets back through adapter
        tcpStack.OnPacketSend = packet => {
            try {
                // ReSharper disable once AccessToDisposedClosure
                adapter.SendPacketQueued(packet);
            }
            catch (Exception ex) {
                Console.WriteLine($"[TCP STACK -> ADAPTER] Error sending packet: {ex.Message}");
            }
        };

        // Setup echo server on our TCP stack
        var listener = tcpStack.Listen(new IpEndPointValue(TestServerIp, TestServerPort));
        Console.WriteLine($"[TEST] Echo server listener created on {TestServerIp}:{TestServerPort}");

        _ = StartEchoServer(listener, completionSource);

        try {
            // Configure and start adapter
            var options = new VpnAdapterOptions {
                SessionName = "TestSession",
                VirtualIpNetworkV4 = IpNetwork.Parse("10.0.0.0/24"),
                IncludeNetworks = [new IpNetwork(TestServerIp, 32)]
            };

            Console.WriteLine("[TEST] Starting WinDivert adapter...");
            Console.WriteLine($"[TEST] Include networks: {string.Join(", ", options.IncludeNetworks)}");
            await adapter.Start(options, CancellationToken.None);
            Console.WriteLine("[TEST] Adapter started successfully");

            // Act - Connect with TcpClient and send/receive data
            using var tcpClient = new TcpClient();

            // Allow WinDivert to settle after adapter start (helps when running after other WinDivert tests)
            await Task.Delay(200);

            Console.WriteLine($"[TEST] Connecting to {TestServerIp}:{TestServerPort}...");

            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await tcpClient.ConnectAsync(TestServerIp, TestServerPort, connectCts.Token);
            Console.WriteLine("[TEST] Connected successfully!");

            await using var stream = tcpClient.GetStream();

            // Buffer-all-then-echo: start receive first, send everything, half-close, then await echo.
            const int chunkSize = 65536;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(150));

            // Start the reception loop before sending so it is ready when the server echoes data back.
            var receiveTask = ReceiveDataInChunks(stream, receivedData, TestDataSize);

            // Send all data, then half-close the send side so the echo server sees EOF and starts echoing.
            await SendDataInChunks(stream, testData, chunkSize).WaitAsync(cts.Token);
            tcpClient.Client.Shutdown(SocketShutdown.Send);

            // Wait for all echoed data to arrive.
            await receiveTask.WaitAsync(cts.Token);

            // Signal completion
            completionSource.SetResult(true);

            // Assert
            Assert.AreEqual(TestDataSize, receivedData.Count, "Received data size should match sent data size");

            var receivedArray = receivedData.ToArray();
            // Use Span-based comparison instead of CollectionAssert.AreEqual which is O(N) but slow for large arrays.
            Assert.IsTrue(testData.AsSpan().SequenceEqual(receivedArray), "Received data should match sent data exactly");

            Console.WriteLine($"[TEST] ✅ Test passed! Successfully echoed {TestDataSize:N0} bytes through TCP stack");
            Console.WriteLine($"[TEST] Total packets received: {packetCount}, TCP packets: {tcpPacketCount}");
        }
        catch (Exception ex) {
            Console.WriteLine($"[TEST] ❌ Test failed with exception: {ex.Message}");
            Console.WriteLine($"[TEST] Stack trace: {ex.StackTrace}");
            throw;
        }
        finally {
            try {
                Console.WriteLine("[TEST] Stopping adapter...");
                adapter.Stop();
            }
            catch (Exception ex) {
                Console.WriteLine($"[TEST] Error stopping adapter: {ex.Message}");
            }
        }
    }

    private static Task StartEchoServer(LocalTcpListener listener, TaskCompletionSource<bool> completionSource)
    {
        return Task.Run(async () => {
            try {
                Console.WriteLine("[ECHO SERVER] Starting...");
                Console.WriteLine($"[ECHO SERVER] Listening on {listener.LocalEndPoint}");

                await foreach (var stream in listener.AcceptAllAsync()) {
                    Console.WriteLine("[ECHO SERVER] ✅ New connection accepted!");

                    // Handle connection in background
                    _ = Task.Run(async () => {
                        try {
                            Console.WriteLine("[ECHO SERVER] Connection handler started - buffering all data before echoing");
                            var buffer = new byte[65536];
                            using var ms = new MemoryStream();

                            // Buffer ALL incoming data until the client half-closes (EOF).
                            while (true) {
                                var bytesRead = await stream.Stream.ReadAsync(buffer, 0, buffer.Length);
                                if (bytesRead == 0) break;
                                ms.Write(buffer, 0, bytesRead);
                            }

                            Console.WriteLine($"[ECHO SERVER] Received all {ms.Length:N0} bytes. Sending echo...");

                            // Echo all buffered data back in one shot.
                            ms.Position = 0;
                            await ms.CopyToAsync(stream.Stream);

                            Console.WriteLine($"[ECHO SERVER] Echo complete. Total echoed: {ms.Length:N0} bytes");
                            await stream.DisposeAsync();
                        }
                        catch (Exception ex) {
                            Console.WriteLine($"[ECHO SERVER] Connection error: {ex.Message}");
                            Console.WriteLine($"[ECHO SERVER] Stack trace: {ex.StackTrace}");
                        }
                    });

                    // Break after handling first connection for this test
                    break;
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"[ECHO SERVER] Error: {ex.Message}");
                Console.WriteLine($"[ECHO SERVER] Stack trace: {ex.StackTrace}");
                completionSource.SetException(ex);
            }
        });
    }

    private static async Task SendDataInChunks(NetworkStream stream, byte[] data, int chunkSize)
    {
        var totalSent = 0;
        var nextLogMb = 50L;

        for (var offset = 0; offset < data.Length; offset += chunkSize) {
            var currentChunkSize = Math.Min(chunkSize, data.Length - offset);
            await stream.WriteAsync(data.AsMemory(offset, currentChunkSize));
            totalSent += currentChunkSize;

            // Log every 50MB only - excessive logging slows the test significantly.
            if (totalSent / (1024 * 1024) >= nextLogMb) {
                Console.WriteLine($"[CLIENT] Sent {totalSent:N0} bytes so far...");
                nextLogMb += 50;
            }
        }

        Console.WriteLine($"[CLIENT] Finished sending {totalSent:N0} bytes");
    }

    private static async Task ReceiveDataInChunks(NetworkStream stream, List<byte> receivedData, int expectedSize)
    {
        // Pre-allocate a flat byte[] for fast appending; copy into List<byte> at the end.
        var pooled = new byte[expectedSize];
        var buffer = new byte[8192];
        var totalReceived = 0;
        var nextLogMb = 50L;

        while (totalReceived < expectedSize) {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, expectedSize - totalReceived)));
            if (bytesRead == 0) break;

            Buffer.BlockCopy(buffer, 0, pooled, totalReceived, bytesRead);
            totalReceived += bytesRead;

            // Log every 50MB only - excessive logging slows the test significantly.
            if (totalReceived / (1024 * 1024) >= nextLogMb) {
                Console.WriteLine($"[CLIENT] Received {totalReceived:N0} bytes so far...");
                nextLogMb += 50;
            }
        }

        // Bulk-add to the caller's List<byte>.
        receivedData.Capacity = totalReceived;
        receivedData.AddRange(pooled.AsSpan(0, totalReceived));

        Console.WriteLine($"[CLIENT] Finished receiving {totalReceived:N0} bytes");
    }

    private static byte[] GenerateRandomTestData(int size)
    {
        var data = new byte[size];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(data);
        return data;
    }
}
