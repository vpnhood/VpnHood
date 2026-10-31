# Protocol 15 request authentication

Protocol 15 proves possession of the farm secret before the server classifies a tunnel request,
reads its body, or accepts a WebSocket upgrade. TCP and QUIC use the same
`ClientRequestAuthenticator` proof check. Rejected TCP proofs return through the ordinary web front;
authentication failures add no error details to its 401 response. On QUIC, rejected input goes through
the HTTP/3 front. It never receives a plaintext HTTP/1 authentication response.

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

## QUIC admission and HTTP/3

`Listeners/Http3` owns the anonymous HTTP/3 endpoint: frame handling, bounded static/literal/Huffman
QPACK decoding, request validation and response encoding. It uses the existing QUIC provider and adds
no production framework or package dependency. Kestrel is only the differential-test reference.

Anonymous streams use a pooled 4 KB reader. HEADERS are decoded incrementally instead of allocating
their declared size; QPACK strings grow only as bytes arrive, within fixed limits, and no dynamic table is kept.
After headers, at most 64 KB of already available input is inspected before the final response stops
further reads. This also bounds a continuous sequence of empty DATA frames. Pending reads retain their
own buffer until cancellation is observed, so returning a temporary decode buffer cannot race native I/O.

Existing clients still send HTTP/1.1 headers and custom tunnel framing inside QUIC. A separate admission
gate captures at most 4096 bytes and verifies a protocol-15 proof before handing that stream to the
existing tunnel. Every new stream must prove itself, even on a connection with an authenticated stream.
The prefix buffer returns after replay; normal tunnel traffic performs no HTTP/3 parsing or proof work.
QUIC always requires HTTP protocol 15. Older HTTP clients receive no QUIC port and use TCP during rollout;
an HTTP-15 client negotiating an older binary session can still use QUIC.

Unauthenticated QUIC connections share the server's global/address connection budget with TCP, using
separate connection identities because multiple QUIC connections can share one UDP endpoint tuple.
The first valid proof releases that connection's budget entry, without bypassing later stream checks.
Active tunnels remain tracked until their owner disposes them, so HTTP idle expiry does not cut them off.

A later proof clock error cannot expose a custom HTTP/1 Date response on QUIC. After a failed fresh QUIC
request, the next fresh request first obtains a Date through the existing certificate-validated TLS path.
Concurrent recoveries share one successful check; cancellation leaves recovery pending. The failed
application request is not replayed, and application traffic stays on QUIC.

Android and iOS keep their native providers and wire format. The server's control-stream open is
independent of incoming stream acceptance, including clients with zero incoming unidirectional credit.
Incoming native streams are released if mobile connection setup fails or its queue closes.

Automatic Alt-Svc advertisement remains off while this endpoint does not replace the entire HTTPS
origin, including downloads. Moving the actual tunnel to standard HTTP/3 through Cloudflare or other
web frontends is separate future work; proxy support must be verified for the chosen tunnel method.

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

HTTP/3 behavior is checked against the installed reference version, including malformed probes; this
does not promise complete undetectability. An accepted statistical difference remains for uncontrolled
bursts of headers and large trailers near the shared 64 KiB parsing allowance. Kestrel RC3 races its
application pipeline against trailer parsing; this front gives response dispatch priority. Repeated
probes can distinguish their proportions of 401, 431 and resets. RC3's proportions depend on host and
load, so the front does not add an artificial delay to imitate them. Controlled cases check both
orders. Burst diagnostics print both distributions and assert only when RC3's complete 401 response
has at least two thirds of the samples. GOAWAY drain is compared without an artificial wake-up.
Native Android/iOS device interoperability and memory
measurements remain release checks. Initial stream padding, published packages and the end-user wiki
rollout remain separate plan stages.
