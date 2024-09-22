using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace VpnHood.NetTester.Testers.HttpTesters;

public static class HttpClientUtil
{
    // Create a custom HttpClient with custom SocketsHttpHandler
    // to resolve the domain and accept all certificates
    // for this example we are not validating the certificate
    public static HttpClient CreateHttpClient(IPAddress? ipAddress, TimeSpan? timeout)
    {
        if (ipAddress == null) {
            var httpClientHandler = new HttpClientHandler();
            httpClientHandler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            httpClientHandler.ClientCertificateOptions = ClientCertificateOption.Manual;
            return new HttpClient(httpClientHandler) {
                Timeout = timeout ?? TimeSpan.FromSeconds(15)
            };
        }

        var socketHttpHandler = new SocketsHttpHandler {
            ConnectCallback = async (context, cancellationToken) => {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) {
                    NoDelay = true
                };

                try {
                    await socket.ConnectAsync(ipAddress, context.DnsEndPoint.Port, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch {
                    socket.Dispose();
                    throw;
                }
            },
            SslOptions = new SslClientAuthenticationOptions {
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                RemoteCertificateValidationCallback = (_, _, _, _) =>
                    true
            }
        };

        var client = new HttpClient(socketHttpHandler) {
            Timeout = timeout ?? TimeSpan.FromSeconds(15)
        };
        return client;

    }
}