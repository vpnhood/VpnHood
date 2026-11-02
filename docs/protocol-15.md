# Protocol 15 request authentication

Protocol 15 proves possession of the farm secret before the server classifies a tunnel request,
reads its body, or accepts a WebSocket upgrade. TCP and QUIC use the same
`ClientRequestAuthenticator` proof check. Rejected TCP proofs return through the ordinary web front;
authentication failures add no error details to its 401 response. On QUIC, rejected input goes through
the HTTP/3 front. It never receives a plaintext HTTP/1 authentication response.

## Proof and lifetime

`RequestProof` is shared by client and server. It creates an HS256 JWT in the `Authorization:
Bearer` header, with exactly four claims: `purpose = vpnhood-request`, `version = 15`, `iat`
(issued-at time), and `exp` (expiration five minutes later). Verification allows 30 seconds of clock
skew. The proof authorizes the farm through its signing key; it contains no hostname or key identifier.
A reverse proxy must preserve Authorization, but may replace the public HTTP Host with its origin's
name. This supports CloudFront routes using `AllViewerExceptHostHeader`; the origin certificate can
match the origin hostname rather than the viewer's `cloudfront.net` name. TLS certificate validation
on the client and proxy remains unchanged.

The signing key is 32 bytes derived using HKDF-SHA256, empty salt, and UTF-8 info
`VpnHood.Protocol15.RequestProof.HS256`. The input is the existing farm secret from
`ServerToken.Secret`. Its length is unchanged because the access-key encryption format also
uses it. The Kestrel reference derives the same signing key without a hostname audience check. Its
positive controls accept valid proofs and reject expired proofs with either the original or a rewritten
Host, ensuring expired-token tests actually reach lifetime validation.

`ClientRequestSigner` derives the client key once and caches a proof until either
`Environment.TickCount64` or corrected wall-clock age reaches four minutes. A wall-clock change cannot
postpone refresh, and wall-clock age also catches expiration during sleep. The sender and connector
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
The operating-system clock never changes, and only the hello may change the HTTP protocol after a
401, as "Rollout and compatibility" says. Other HTTP headers and any binary body remain available to callers of `HttpUtils.ReadResponse`.

Redirects stay in the access key's farm. A redirect token without a `Secret` uses the key's
`ServerToken.Secret`, even when its hostname differs. A saved server after a crash is tried with
that same farm secret before discovery. ServerCheck returns 401 even for a valid proof, so a 401
means the endpoint is reachable. If the saved server refuses the subsequent hello, that connect
fails; closing the client deletes the saved endpoint, so the next connect searches normally.

## Rollout and compatibility

The earlier protocol-15 proof shape containing `host`, shipped in the 9.0.860 NuGet packages from
`e097474e1` and the server prerelease from `6894fabdf`, is rejected. Update clients and servers using
that shape together: the HTTP version number
does not distinguish the two proof formats, and either mixed pair fails authentication. Publish the
current source under a new package version. Compatibility with HTTP versions 8–14 remains as follows.

1. Servers and apps update in either order. A protocol-15 app sends HTTP 15 with its proof. A
   server older than 15 answers that with a 401, which the app cannot tell from a refused proof, so
   its hello tries the legacy HTTP once: no proof, and version 8 until the hello agrees on a binary
   version, then that one below 15. The app remembers, per server and farm and for its process,
   which way worked. A server that took 15 is never asked the legacy way again, so no 401 talks the
   app down from the proof there; one that took only the legacy way is asked that way first, and 15
   again once it refuses that. A legacy session carries no proof and no padding, gets the hello's
   farm secret as older apps do, and a QUIC port only from a server older than 15.
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

The `VpnHood.Net.Http3` project owns the anonymous HTTP/3 endpoint: frame handling, bounded
static/literal/Huffman QPACK decoding, request validation and response encoding. It knows nothing of
VpnHood: the server plugs in a stream filter (the proof check), a runner for the streams it claims (the
tunnel) and a responder (the 401), and its unauthenticated budget closes connections through it. It uses
the existing QUIC provider and adds no production framework or package dependency. Kestrel is only the
differential-test reference.

Anonymous request streams start with a pooled 4 KB reader that grows to 16 KB only after a full
buffer is received and consumed. Unidirectional control and QPACK readers remain at 4 KB. HEADERS are
decoded incrementally instead of allocating their declared size; QPACK strings grow only as bytes
arrive, within fixed limits, and no dynamic table is kept. After headers, the parser inspects at most
64 KB while the queued response runs; response completion stops further reads. This also bounds a
continuous sequence of empty DATA frames.

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

A later proof clock error cannot expose a custom HTTP/1 Date response on QUIC. After failed admission
on an established QUIC stream, a subsequent request makes a best-effort Date probe through the existing
certificate-validated TLS path, with a two-second deadline. Concurrent requests continue without waiting.
A failed probe leaves recovery pending with a one-minute retry cooldown; the application request still
gets its full timeout and stays on QUIC. Connect failures, stream-open failures and caller cancellation
do not schedule recovery. The failed application request is not replayed.

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

The local TLS tests exercise signed POSTs, upgrades, proxy-forwarded request headers with a rewritten
Host, secret rotation, clock correction and status handling. They do not test a deployed CloudFront
route. Before deployment, verify Authorization forwarding and the configured origin Host on the actual
route with a valid request and a correctly signed expired request, and confirm the latter gets the
ordinary 401. Never put proofs or access keys in URLs or deployment logs.

HTTP/3 behavior is checked against the installed reference version, including malformed probes; this
does not promise complete undetectability. The application is queued after complete request headers,
while the parser continues through DATA, frames and trailers. Request metadata is validated before
the application freezes the live header/trailer limit decision. Application completion, submission of
response bytes with FIN, native write completion and transport abort completion are separate events.
The response dispatch joins its writes and abort callbacks during asynchronous disposal before the
request owner releases the stream. A protocol abort precedes native I/O cancellation so cancellation
cannot replace the intended error code.

Response submission stops a newly pending read; a read already in flight may finish while the native
response write is pending. After that write completes, the parser can still consume bytes returned
without waiting, then aborts the first pending read with H3_NO_ERROR. Inspection also closes unfinished
input after its 64 KB byte budget. The pooled request reader starts
at 4 KB and grows to 16 KB only after receiving and consuming a full buffer; control and QPACK readers
stay at 4 KB. A large advertised frame length does not reserve a large buffer. Buffered QPACK names
and values decode in place, with bounded incremental decoding for fragmented fields. Authenticated
tunnels bypass this reader and scheduling entirely.

Controlled tests gate input bytes or response submission at parser/application boundaries. Burst
comparisons sample independent connection pairs, reuse each pair for 20 streams, and retain every
observation. Every response must use an allowed HTTP/3 outcome, error code and scope; complete
responses must match the reference's wire form apart from Date. Controlled parser-first and
response-first cases remain release gates. Every burst row defaults to 200 samples, and separate
500-stream tests check cleanup and reuse.

Uncontrolled burst shares and sampled outcome presence are diagnostics. The historical 22-point
share band is reported without deciding pass/fail: the same RC3 reference changes its shares with
CPU availability and scheduling. Paired host and VM measurements on 2026-11-01 confirmed that
dependence. A single racing request has several valid outcomes; matching one host's percentages
is not a protocol requirement. Keep these observations for investigating persistent differences,
without adding delays or parser scheduling rules to fit them.

Post-header scheduling is implemented and tested locally. The final 2026-11-01 full run passed
228/228 cases and all 669 TCP probes, including six added real-QUIC held-application checks for
uppercase and malformed-QPACK trailers. The 65,537-byte reset share remained outside the historical
band and is retained as a diagnostic. The committed production implementation was unchanged;
experimental timing adjustments were rejected. Rerun the full suite on the release candidate.
The same built test output passed all 25 selected burst/controlled-error cases on the Windows
Server 2025 Desktop VM with .NET 11 RC3.
Native Android/iOS device interoperability and memory measurements remain release checks. The
[release checklist](ip-blocking-release.md) covers full-suite validation, package/dependent-reference
checks and the end-user documentation to publish alongside the enforcing server release.

## Initial stream padding

Each side keeps padding eligibility in one place; connection owners make no padding calls.
A background write failure stops further writes and prevents reuse.

`SessionOptions.StartPadding` enables initial padding for WebSocket TCP proxy streams. It defaults off;
an explicit `false` also overrides an inherited `true`. Hello advertises `StartPadding` to HTTP-15 clients.
The client records the successful hello's flag and offers `X-WebSocketMasking: true` on subsequent
WebSocket upgrades only for that session, excluding connections opened for TCP packet channels.
Connections opened before hello, including ServerFinder checks, keep unmasked payloads. Plain HTTP,
no-handshake POST streams and packet channels keep unmasked payloads and receive no padding.
Control requests receive no padding.

Masking is fixed by the upgrade request: the client masks if it offered; the server unmasks if the
request offered at HTTP 15. The 101 carries no masking echo, so a proxy dropping unknown response
headers cannot change framing. Mask keys come from `Random.Shared`. The server pads an authenticated
proxy stream only when its session enables padding and its WebSocket stream carries this masking
agreement. Reuse retains the agreement and starts a fresh padding budget in both directions, without
depending on the initial HTTP connection wrapper.

Compatibility (2026-10): unmasked client frames retain the MASK flag and key fields for older peers;
those header fields can be dropped when those peers are no longer supported. Their payloads stay
unmasked unless the request offered masking. The session's flag never changes an already-open
connection's masking.

The first eligible real write and a printable ASCII text frame are encoded into one source write,
in each direction. This includes the client's proxy request and embedded inner hello and the server's
initial session response. The temporary pooled buffer is at most 16 KiB; no large padding buffer is
retained on an idle stream. Ordinary writes copy into writer-owned storage; preserved-buffer writes
may overwrite both header space and payload, including on failure. Neither path restores masked bytes.
Text, ping and pong frames are discarded without replies. Interleaved control frames do not disrupt
fragmented binary or text messages; close frames record the status and prevent reuse.

Current experimental limits per direction and logical stream are:

- A randomly drawn 4–12 KiB padding budget, counting text frame headers conservatively.
- Two to five eligible initial writes, with 128–3072 bytes of text each. Writes over 12 KiB are skipped.
- One to three gap opportunities after the first padded write, separated by 40–350 ms. Each may add
  128–1400 bytes from the same budget. An interval with real writes, or a busy writer, skips that opportunity.
- No new padding after two seconds from the first real write; a slow destination connect does not
  consume the window before the server's initial response. After the write allowance is exhausted, data
  writes bypass padding. Real traffic never waits for a cover timer.

Only streams whose cover task can still write take a write gate. Data and cover frames cannot interleave;
after cover finishes, publication of the gate-free path follows its last physical write. Streams with
padding off, including packet channels, never take this gate. Inline data/text padding may spend its
remaining budget after cover has finished without a gate, since it has no concurrent writer.

These ranges are a bounded starting experiment, not a calibrated distribution of real page loads.
At most 24 KiB of text-frame overhead can be added across both directions of one logical stream.
Graceful stream close cancels cover and joins it under the existing close deadline before sending the
logical terminator; a canceled or failed frame prevents reuse. Closing the transport fails any pending
cover write, which ends the loop quietly without a padding call from the connection owner. The padding
window limits new cover frames; it does not cancel an in-flight write on a slow connection. A pending
transport write owns its rented memory until that write ends, even if close times out.

Local TLS record measurements with padding off and on showed that padding enlarges the first record
in both directions on a new connection and seven reuses. Record boundaries remain an OS/TLS
implementation detail. Native Android/iOS and other deployment-platform captures, realistic page-load
bandwidth measurements and calibration remain required before enabling this experiment broadly.
