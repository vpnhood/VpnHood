using System.Net.Security;
using CoreFoundation;
using Security;

namespace VpnHood.Core.Quic.Ios;

/// <summary>
/// Bridges VpnHood's <see cref="RemoteCertificateValidationCallback"/> (the desktop pinned-certificate
/// model) onto Network. Framework's TLS verify block.
/// </summary>
/// <remarks>
/// VpnHood pins the server certificate by hash, so the system trust evaluation alone is not enough —
/// we must hand the actual leaf certificate to the supplied callback. The exact <c>SetVerifyBlock</c>
/// delegate shape and <see cref="SecTrust"/> accessors are the most binding-version-sensitive part of
/// the iOS QUIC implementation and should be confirmed when first compiled on a Mac.
/// </remarks>
internal static class IosQuicTls
{
    public static void Configure(
        SecProtocolOptions secOptions,
        string targetHost,
        RemoteCertificateValidationCallback certificateValidationCallback,
        DispatchQueue queue)
    {
        // Match the desktop client: TLS 1.3 only, SNI = the VpnHood host name, peer auth required.
        secOptions.SetTlsServerName(targetHost);
        secOptions.SetTlsMinVersion(TlsProtocolVersion.Tls13);
        secOptions.SetPeerAuthenticationRequired(true);

        secOptions.SetVerifyBlock((_, trust, complete) => {
            bool isValid;
            try {
                isValid = Validate(trust, certificateValidationCallback);
            }
            catch {
                isValid = false;
            }
            complete(isValid);
        }, queue);
    }

    private static bool Validate(SecTrust2 secTrust2, RemoteCertificateValidationCallback callback)
    {
        using var trust = secTrust2.Trust;

        // Leaf (server) certificate is at index 0 of the evaluated chain (iOS 15+ API).
        var certificates = trust.GetCertificateChain();
        if (certificates is not { Length: > 0 })
            return false;

        using var leaf = certificates[0].ToX509Certificate2();

        // Run the system trust evaluation so the callback receives meaningful SslPolicyErrors
        // (None when the chain is system-trusted, RemoteCertificateChainErrors otherwise). VpnHood's
        // own callback then falls back to the pinned-hash comparison for self-signed servers.
        var policyErrors = trust.Evaluate(out _)
            ? SslPolicyErrors.None
            : SslPolicyErrors.RemoteCertificateChainErrors;

        return callback(sender: secTrust2, certificate: leaf, chain: null, sslPolicyErrors: policyErrors);
    }
}
