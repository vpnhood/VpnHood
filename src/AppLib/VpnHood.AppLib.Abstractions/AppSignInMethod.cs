using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Abstractions;

[JsonConverter(typeof(JsonStringEnumConverter<AppSignInMethod>))]
public enum AppSignInMethod
{
    Google,
    Apple,
    UsernamePassword
}
