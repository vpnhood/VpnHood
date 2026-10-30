# Protocol 15 request authentication

Protocol 15 proves possession of the farm secret before the server classifies a tunnel request,
reads its body, or accepts a WebSocket upgrade. TCP and QUIC use the same
`ClientRequestAuthenticator` gate. Rejected proofs return through the ordinary web front;
authentication failures add no error details to its 401 response.

## Proof and lifetime

`RequestProof` is shared by client and server. It creates an HS256 JWT in the `Authorization:
Bearer` header, with `purpose = vpnhood-request`, `version = 15`, normalized HTTP hostname,
issued-at time, and expiration five minutes later. Verification allows 30 seconds of clock skew.
The host ignores DNS case, a trailing dot and the port; a reverse proxy must preserve the public
HTTP Host and Authorization headers. Its backend TLS certificate may have a different name.

The signing key is 32 bytes derived using HKDF-SHA256, empty salt, and UTF-8 info
`VpnHood.Protocol15.RequestProof.HS256`. The input is the existing farm secret from
`ServerToken.Secret`. Its length is unchanged because the access-key encryption format also
uses it. The Kestrel reference derives the same signing key, and a valid-token positive control
ensures expired-token tests actually reach lifetime validation.

`ClientRequestSigner` derives the client key once and caches a proof for four minutes measured by
`Environment.TickCount64`, so a wall-clock change cannot postpone refresh. The sender and connector
receive the same signer through their constructors. The connector selects the proof after connection
setup and TLS, immediately before writing the HTTP head, so connection waits cannot age it before
selection. Reused connections do not sign a proof. The server derives its key when configuration
changes. Established packet and stream data do no proof work. There is no replay-token database.
A captured proof can be reused within its lifetime; anyone holding an access key can mint proofs.

A 401's Date header corrects the signer's clock whenever it differs from the corrected clock
by more than the 30-second tolerance, including after another device-clock change. There is no
offset cap: the Date comes from the verified TLS peer. Each request retries at most once, with
the original cancellation deadline. `RequestSender` records the signer's correction number before
the attempt. If another request corrects the clock meanwhile, a 401 permits that one retry without
applying its potentially stale Date. A correction before the proof is selected can permit an
unnecessary retry, still within the one-retry limit. Authorization exceptions carry only the server's
Date; no correction number crosses the transport, and the signing key stays in the signer.
The operating-system clock never changes, and a 401 never downgrades the HTTP protocol. Other
HTTP headers and any binary body remain available to callers of `HttpUtils.ReadResponse`.

Redirects stay in the access key's farm. A redirect token without a `Secret` uses the key's
`ServerToken.Secret`, even when its hostname differs. A saved server after a crash is tried with
that same farm secret before discovery. ServerCheck returns 401 even for a valid proof, so a 401
means the endpoint is reachable. If the saved server refuses the subsequent hello, that connect
fails; closing the client deletes the saved endpoint, so the next connect searches normally.

## Rollout and compatibility

1. Update servers before deploying protocol-15 clients. These clients send HTTP version 15 from
   the initial probe and hello onward and cannot connect to an older server that rejects it.
2. Update users' apps. The server's default minimum stays at 8 during rollout, allowing older
   clients' existing unauthenticated HTTP entry path. With a minimum below 15, the gate admits
   HTTP versions 8–14 without a proof; after checking the key, the hello checks the negotiated
   binary version against the minimum and tells an older app to update (`UnsupportedClient`).
3. Set `SessionOptions.MinClientProtocolVersion` to 15, or the corresponding setting in a MANAGER
   server profile, after users have updated. The server then rejects older versions before a body
   or upgrade, closing that entry path. At a minimum of 15 or higher, an older app gets the plain
   401 with no update message. Setting 15 too soon disconnects older clients.

HTTP authentication version and binary session version are separate. A protocol-15 HTTP client
can negotiate a binary session version from 8 through 15 with an access manager. Subsequent
HTTP requests still use 15. The hello omits the standalone `ServerSecret` field whenever its HTTP
version is 15, including when the binary session is older. Legacy HTTP clients still receive it.

## Scope and remaining deployment checks

Omitting `ServerSecret` removes a redundant secret from the ordinary hello; it does **not** promise
that a TLS-terminating proxy can never see a farm secret. Existing access-key updates in
`AccessKey` and farm redirects in `RedirectServerTokens` can carry secrets. Protecting that key
distribution requires its own protocol design. Do not describe JWT expiry as limiting a proxy
that has received a long-lived key through those paths.

The local TLS tests exercise signed POSTs, upgrades, ordinary proxy-forwarded request headers,
secret rotation, clock correction and status handling. They do not test a deployed Cloudflare
route. Before deployment, verify Authorization and Host forwarding on the actual route with a
valid request and a correctly signed expired request, and confirm the latter gets the ordinary
401. Never put proofs or access keys in URLs or deployment logs.

HTTP/3 behavior, initial stream padding, platform measurements, published packages and the
end-user wiki rollout remain separate plan stages. This change does not enable them.
