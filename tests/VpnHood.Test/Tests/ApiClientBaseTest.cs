using System.Globalization;
using System.Net;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class ApiClientBaseTest : TestBase
{
    private enum Speed
    {
        Slow = 7
    }

    [Flags]
    private enum Feature
    {
        One = 1,
        [EnumMember(Value = "second")] Two = 2
    }

    private sealed record Item(string Name, int Count);

    private sealed class QueryApiClient : ApiClientBase
    {
        public string ToQuery(object value)
        {
            return ConvertToString(value, CultureInfo.InvariantCulture);
        }
    }

    // No context of its own, and settings it changes as it is made, as the access server's agent clients do
    private sealed class CamelCaseApiClient : ApiClientBase
    {
        public CamelCaseApiClient(HttpClient httpClient) : base(httpClient)
        {
            JsonSerializerSettings.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        }

        public Task<Item> Post(Item item, Speed speed, CancellationToken cancellationToken)
        {
            return HttpPostAsync<Item>("test", new Dictionary<string, object?> { ["speed"] = speed }, item,
                cancellationToken);
        }
    }

    // Its body, its reply and its enum, all under the settings the client changed, with no resolver of its own
    [TestMethod]
    public async Task A_client_with_no_context_keeps_its_settings_and_sends_an_enum_as_its_number()
    {
        string? requestBody = null;
        string? requestQuery = null;
        using var httpClient = CreateHttpClient(async (request, cancellationToken) => {
            var content = request.Content ?? throw new InvalidOperationException("The call has no body.");
            requestBody = await content.ReadAsStringAsync(cancellationToken);
            requestQuery = request.RequestUri?.Query;
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("""{"name":"back","count":2}""", Encoding.UTF8, "application/json")
            };
        });

        var item = await new CamelCaseApiClient(httpClient).Post(new Item("out", 1), Speed.Slow, CancellationToken.None);

        Assert.AreEqual("""{"name":"out","count":1}""", requestBody);
        Assert.AreEqual("?speed=7", requestQuery);
        Assert.AreEqual(new Item("back", 2), item);
    }

    // A value with no EnumMember goes as its number, and a combination, which has no name of its own, by its names
    [TestMethod]
    public void An_enum_in_a_query_goes_by_its_EnumMember()
    {
        var client = new QueryApiClient();

        Assert.AreEqual("second", client.ToQuery(Feature.Two));
        Assert.AreEqual("1", client.ToQuery(Feature.One));
        Assert.AreEqual("One, Two", client.ToQuery(Feature.One | Feature.Two));
    }

    // .NET's default casing is what it writes; a web host's camelCase is read as well
    [TestMethod]
    public void An_ApiError_writes_its_names_as_they_are_and_reads_either_casing()
    {
        var apiError = new ApiError { TypeName = "Type1", Message = "Message1", Data = { ["Key1"] = "Value1" } };

        var json = apiError.ToJson();

        Assert.Contains("\"TypeName\":\"Type1\"", json);
        Assert.IsTrue(ApiError.TryParse(json, out var parsed));
        Assert.AreEqual(apiError, parsed);

        Assert.IsTrue(ApiError.TryParse("""{"typeName":"Type1","message":"Message1"}""", out var camelCase));
        Assert.AreEqual("Type1", camelCase.TypeName);
        Assert.IsFalse(ApiError.TryParse("""{"message":"no type"}""", out _));
    }

    [TestMethod]
    public void An_ApiError_writes_indented_only_when_asked()
    {
        var apiError = new ApiError { TypeName = "Type1", Message = "Message1" };

        var indented = apiError.ToJson(writeIndented: true);

        Assert.Contains("\n", indented);
        Assert.Contains("\"TypeName\": \"Type1\"", indented);
        Assert.DoesNotContain("\n", apiError.ToJson());
        Assert.IsTrue(ApiError.TryParse(indented, out var parsed));
        Assert.AreEqual(apiError, parsed);
    }

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
