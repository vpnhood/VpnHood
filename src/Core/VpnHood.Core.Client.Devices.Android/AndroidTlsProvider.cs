using Android.App;
using Android.Runtime;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Client.Devices.Android;

// .NET's SslStream asks Android for the handshake's cipher before it calls a certificate callback
// (SslStream.VerifyRemoteCertificateCore), and the client's callback pins the server's certificate.
// Android 7's own TLS has no cipher to give at that point and answers "(NONE)", which .NET cannot
// parse, so no connect got through; Android 8.1's crashes the process there (dotnet/runtime#136272
// skips the ask on 8.1 only), and Android 9's answers SSL_NULL_WITH_NULL_NULL (dotnet/runtime#136456).
// So below Android 10 the VPN service takes its TLS from a current Conscrypt - the library Android's
// own TLS is an old copy of - put first among the providers, where SSLContext.getInstance finds it.
// It brings no trust manager: Android's, under the app's network security config, still checks the server's
// chain, and .NET checks it again against the system's roots alone.
internal static class AndroidTlsProvider
{
    private static readonly Lazy<bool> Installed = new(Install);

    public static void EnsureInstalled() => _ = Installed.Value;

    private static bool Install()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            return false;

        using var conscrypt = Java.Lang.Class.ForName("org.conscrypt.Conscrypt", true, Application.Context.ClassLoader);
        using var newProvider = conscrypt.GetMethod("newProvider");
        using var provider = newProvider.Invoke(null)?.JavaCast<Java.Security.Provider>() ??
                             throw new InvalidOperationException("Conscrypt made no TLS provider.");

        var position = Java.Security.Security.InsertProviderAt(provider, 1);
        VhLogger.Instance.LogInformation("Conscrypt is the TLS provider. Name: {Name}, Position: {Position}",
            provider.Name, position);
        return true;
    }
}