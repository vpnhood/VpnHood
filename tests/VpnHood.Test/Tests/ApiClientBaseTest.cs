using Microsoft.Extensions.Logging;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class ApiClientBaseTest : TestBase
{
    // A call its caller cancels still throws, but is no failure: it leaves no Error in the log
    [TestMethod]
    public async Task A_call_its_caller_cancels_logs_no_error()
    {
        var message = TestLogCapture.Instance.CreateMessage();
        using var cancellationTokenSource = new CancellationTokenSource();
        using var httpClient = CreateHttpClient(async (_, cancellationToken) => {
            await cancellationTokenSource.CancelAsync();
            throw new OperationCanceledException(message, cancellationToken);
        });
        var apiClient = new TestApiClient(httpClient) { Logger = TestLogCapture.Instance };

        await Assert.ThrowsAsync<OperationCanceledException>(() => apiClient.Get(cancellationTokenSource.Token));
        CollectionAssert.DoesNotContain(TestLogCapture.Instance.GetLevels(message).ToArray(), LogLevel.Error);
    }

    // A cancellation its caller did not ask for, as a timeout's, is a failure
    [TestMethod]
    public async Task A_cancellation_its_caller_did_not_ask_for_logs_an_error()
    {
        var message = TestLogCapture.Instance.CreateMessage();
        using var httpClient = CreateHttpClient((_, _) => throw new OperationCanceledException(message));
        var apiClient = new TestApiClient(httpClient) { Logger = TestLogCapture.Instance };

        await Assert.ThrowsAsync<OperationCanceledException>(() => apiClient.Get(CancellationToken.None));
        CollectionAssert.Contains(TestLogCapture.Instance.GetLevels(message).ToArray(), LogLevel.Error);
    }

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    {
        return new HttpClient(new TestHttpMessageHandler(send)) { BaseAddress = new Uri("http://localhost/") };
    }
}
