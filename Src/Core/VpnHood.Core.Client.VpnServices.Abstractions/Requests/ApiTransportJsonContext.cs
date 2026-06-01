using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Requests;

//todo: check ios AOT
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(ConnectionInfo))]
[JsonSerializable(typeof(ApiResponse<object>))]
[JsonSerializable(typeof(ApiAdFailedRequest))]
[JsonSerializable(typeof(ApiDisconnectRequest))]
[JsonSerializable(typeof(ApiGetConnectionInfoRequest))]
[JsonSerializable(typeof(ApiReconfigureRequest))]
[JsonSerializable(typeof(ApiSetAdOkRequest))]
[JsonSerializable(typeof(ApiSetWaitForAdRequest))]
public partial class ApiTransportJsonContext : JsonSerializerContext
{
    public static JsonTypeInfo<T> For<T>()
    {
        return (JsonTypeInfo<T>)Default.GetTypeInfo(typeof(T))!;
    }
}