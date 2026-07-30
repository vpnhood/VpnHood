using System.Text;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;

namespace VpnHood.Test.Tests;

[TestClass]
public class TcpMessageTest
{
    [TestMethod]
    public async Task Client_should_reconnect_after_listener_restart()
    {
        var configFolder = CreateConfigFolder();
        try {
            using var client = new TcpMessageClient(configFolder);

            using (var listener = new TcpMessageListener(configFolder)) {
                var listenerTask = listener.Start(Echo, CancellationToken.None);
                await AssertEcho(client, "first");
                listener.Dispose();
                await listenerTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            using (var listener = new TcpMessageListener(configFolder)) {
                var listenerTask = listener.Start(Echo, CancellationToken.None);
                await AssertEcho(client, "second");
                listener.Dispose();
                await listenerTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally {
            Directory.Delete(configFolder, recursive: true);
        }
    }

    [TestMethod]
    public async Task Client_should_preserve_caller_cancellation()
    {
        var configFolder = CreateConfigFolder();
        try {
            using var listener = new TcpMessageListener(configFolder);
            using var client = new TcpMessageClient(configFolder);
            using var requestCts = new CancellationTokenSource();
            var requestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var listenerTask = listener.Start(async (_, cancellationToken) => {
                requestReceived.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Memory<byte>.Empty;
            }, CancellationToken.None);

            var sendTask = client.SendAsync(new byte[] { 1 }, requestCts.Token);
            await requestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5), requestCts.Token);
            await requestCts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(() => sendTask);

            listener.Dispose();
            await listenerTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally {
            Directory.Delete(configFolder, recursive: true);
        }
    }

    private static Task<Memory<byte>> Echo(Memory<byte> request, CancellationToken cancellationToken)
    {
        return Task.FromResult(request);
    }

    private static async Task AssertEcho(TcpMessageClient client, string message)
    {
        var request = Encoding.UTF8.GetBytes(message);
        var response = await client.SendAsync(request, CancellationToken.None);
        CollectionAssert.AreEqual(request, response.ToArray());
    }

    private static string CreateConfigFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), nameof(TcpMessageTest), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
