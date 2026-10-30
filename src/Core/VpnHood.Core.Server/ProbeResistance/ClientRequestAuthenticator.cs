using System.Net.Http.Headers;
using VpnHood.Core.Tunneling.ProbeResistance;

namespace VpnHood.Core.Server.ProbeResistance;

// This check precedes protocol classification, body reads and upgrades on both TCP and QUIC.
// A failure returns to the ordinary web front, so authentication details never change its 401.
internal static class ClientRequestAuthenticator
{
    public static bool TryAuthenticate(HttpRequestHead request, RequestProof proof, int minimumVersion,
        int maximumVersion, out int protocolVersion)
    {
        // Legacy hellos use HTTP 8 regardless of their binary capabilities. Only the hello can
        // enforce a minimum below 15 and tell an authenticated client to update.
        var minimumHttpVersion = minimumVersion < RequestProof.ProtocolVersion
            ? ServerHost.MinProtocolVersion
            : minimumVersion;
        if (!int.TryParse(request.Headers.GetValueOrDefault("X-ProtocolVersion"), out protocolVersion) ||
            protocolVersion < minimumHttpVersion || protocolVersion > maximumVersion)
            return false;

        // Compatibility (2026-10): old clients have no header proof. An administrator ends this
        // exception by raising MinClientProtocolVersion to 15 once those clients have been updated.
        if (protocolVersion < RequestProof.ProtocolVersion)
            return true;

        if (!AuthenticationHeaderValue.TryParse(request.Headers.GetValueOrDefault("Authorization"), out var authorization) ||
            !authorization.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(authorization.Parameter))
            return false;

        // Bind to the HTTP authority the client signed. A proxy's public hostname need not be
        // the backend certificate's name; the shared farm key authorizes the farm, not one node.
        return proof.Verify(authorization.Parameter, request.Headers.GetValueOrDefault("Host", ""),
            protocolVersion, DateTimeOffset.UtcNow);
    }
}
