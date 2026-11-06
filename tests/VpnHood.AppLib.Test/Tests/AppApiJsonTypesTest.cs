using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.HttpClients;
using VpnHood.Net.Toolkit.ApiClients;

namespace VpnHood.AppLib.Test.Tests;

// The browser page reads and writes the app's API only through AppApiJsonContext, with reflection off. A type missing
// from that list gives no warning when the page is published: it throws when the page makes that call. The publish
// workflows run this class by name and stop on it.
[TestClass]
public class AppApiJsonTypesTest
{
    // The JSON null answers every call, as finding the reply's type is all that counts here. An image comes back as an
    // image, which is read with no JSON at all.
    private sealed class NullReplyHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var isImage = request.RequestUri?.AbsolutePath.EndsWith(".jpg", StringComparison.Ordinal) == true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("null", Encoding.UTF8, isImage ? "image/jpeg" : "application/json")
            });
        }
    }

    // A real object for a body, as a null one is sent as no body and its type would go unchecked
    private static object? CreateArgument(Type type)
    {
        if (type == typeof(string))
            return "x";

        if (type.IsValueType || type.GetConstructor(Type.EmptyTypes) != null)
            return Activator.CreateInstance(type);

        return RuntimeHelpers.GetUninitializedObject(type);
    }

    // Every method of every API in VpnHoodApi, so a new call is covered by being added
    [TestMethod]
    public async Task Every_api_call_finds_its_types_in_the_json_context()
    {
        using var handler = new NullReplyHandler();
        using var httpClient = new HttpClient(handler);
        httpClient.BaseAddress = new Uri("http://localhost/");
        var api = VpnHoodApiHttpFactory.Create(httpClient);
        var failures = new List<string>();

        foreach (var property in typeof(VpnHoodApi).GetProperties()) {
            var client = property.GetValue(api);
            foreach (var method in property.PropertyType.GetMethods()) {
                var name = $"{property.Name}.{method.Name}";
                var callCount = handler.CallCount;
                var arguments = method.GetParameters().Select(x => CreateArgument(x.ParameterType)).ToArray();

                try {
                    var result = method.Invoke(client, BindingFlags.DoNotWrapExceptions, null, arguments, null);
                    await (result as Task ?? throw new InvalidOperationException($"{name} returns no task."));
                }
                catch (NotSupportedException ex) {
                    failures.Add($"{name}: {ex.Message}");
                    continue;
                }
                catch (ApiException) {
                    // the null is no answer for a call that returns a value type, but its type was found
                }

                if (handler.CallCount == callCount)
                    failures.Add($"{name}: never reached the server.");
            }
        }

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }
}
