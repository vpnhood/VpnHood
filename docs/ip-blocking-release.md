# IP-blocking release checks

Use this checklist with [protocol-15.md](protocol-15.md) when preparing a release containing the
request proof, anonymous HTTP/3 front and initial stream padding. Local tests establish integration;
the deployment and device checks below still need evidence from the release candidate.

## Validate the candidate

Record the source commit and any uncommitted source changes with the results. Run the suites
sequentially: they share local ports, certificates, native transports and test storage. On Windows,
run under the developer account with permission to create the test certificates and tunnel locks.
Do not stop another VPN or change its lock permissions to make a test pass.

From the repository root, set the output locations before running any test:

```powershell
$testRoot = Join-Path $env:LOCALAPPDATA 'Temp/vh/ip-blocking'
$env:TMP = Join-Path $testRoot 'tmp'
$env:TEMP = $env:TMP
$testResults = Join-Path $testRoot 'test-results/stage7'
New-Item -ItemType Directory -Force -Path $env:TMP, $testResults | Out-Null
Remove-Item Env:VH_PARITY_PROBES -ErrorAction SilentlyContinue
Remove-Item Env:VH_HTTP3_BURST_SAMPLES -ErrorAction SilentlyContinue
```

Run each command separately and retain its exit code and log. The SDK is selected by `global.json`;
restore first if dependencies are not already available.

```powershell
dotnet test tests/VpnHood.Test --no-restore -m:4 --results-directory $testResults --logger 'trx;LogFileName=core.trx' *> (Join-Path $testRoot 'stage7-core.log')
dotnet test tests/VpnHood.AppLib.Test --no-restore -m:4 --results-directory $testResults --logger 'trx;LogFileName=applib.trx' *> (Join-Path $testRoot 'stage7-applib.log')
dotnet test tests/VpnHood.Test.KestrelParity --no-restore -m:4 --results-directory $testResults --logger 'trx;LogFileName=parity.trx' *> (Join-Path $testRoot 'stage7-parity.log')
```

The parity suite runs both TCP and HTTP/3 against the installed Kestrel reference. Its reports name
that version; retain it with the results. Every burst row defaults to 200 samples, including
DATA-after-trailers. Post-header scheduling is implemented and built locally. Its parity rows have
not been run on this code and must be run before release.

DATA-after-trailers remains an open difference: ours answers 401 more often than the reference.
Post-header distribution acceptance remains open. Run and report all twelve
`Post_header_burst_distributions_match_reference_shares` rows and existing trailer-distribution rows
for the release candidate. Copy the reports under `$env:TMP/vh/VpnHood.Test.KestrelParity` before a
filtered retry overwrites them.

For each failure, distinguish a production difference, a test failure and a host setup failure using
its captured output. An isolated retry is additional evidence; keep the original failure in the report.

Smoke-build the native provider libraries, with the corresponding workloads installed:

```powershell
dotnet build src/Net/VpnHood.Net.Quic.Android --no-restore -m:4
dotnet build src/Net/VpnHood.Net.Quic.Ios --no-restore -m:4 -f net11.0-ios
```

Redirect these logs into `$testRoot` as well. These are library compilation checks, not device or
distribution builds. App release packages and publishing run through GitHub Actions.

## Packages and dependent applications

`VpnHood.Core.Server` references `VpnHood.Net.Http3`; automatic package discovery includes the new
project. The engine depends on Toolkit and the QUIC abstractions. ASP.NET Core/Kestrel belongs only
to the differential-test project, not the production dependency graph.

Before publishing, inspect the packed server and HTTP/3 manifests and their dependency closure.
The server package must depend on HTTP/3 at the candidate version, and neither its manifests nor its
runtime dependencies may add ASP.NET Core/Kestrel. A local Debug pack is only a packaging smoke test;
keep its output outside the repository and never distribute it as a release.

Use the existing [release workflow](../pub/RELEASE-STRATEGY.md) to allocate the next version and
publish the package set. Do not reuse a previously published version for the newer source or update
consumers to a package that has not been published. After publishing, update package consumers,
including MANAGER's server-access contracts and branded app forks, to the same candidate set and
build them. MANAGER and its UI must both gain the new `SessionOptions` contract: an older UI's typed
profile serialization cannot preserve `StartPadding`. Update the template's common `VpnHoodVersion`
too. Client/Connect brand workflows select monorepo source with `code_ref`; their source heads already
use project references. A package version alone
does not prove a deployed server includes these changes: record the source commit of its artifact.

## Deployment and device evidence

| Check | Evidence required before declaring it complete |
| --- | --- |
| Direct TLS and both proxy routes | A valid protocol-15 hello, upgrade, bidirectional tunnel traffic and connection reuse on the actual direct, Cloudflare and CloudFront routes. Record the origin Host behavior. |
| Proof forwarding and expiration | A valid proof succeeds and its correctly signed expired counterpart gets the ordinary 401 through each proxy. Never put access keys or proofs in URLs, logs or committed files. |
| Minimum version | With the candidate configured for minimum 15, legacy requests are refused before a withheld POST body or WebSocket upgrade, including through the proxy. Correlate origin logs if a frontend waits for the body. |
| Direct QUIC | Repeat anonymous and malformed HTTP/3 probes, valid protocol-15 tunnels and reuse against the deployed endpoint. Confirm an invalid stream does not gain admission because another stream authenticated. |
| Android and iOS | On devices, verify native QUIC connections, clock recovery, cancellation, disposal and repeated reconnects; measure retained memory and compare with padding off. Library builds alone do not cover these. |
| Padding captures | Capture outer TLS records with padding off/on in both directions, on fresh and reused connections, on Android, iOS and deployment platforms. Confirm first-record effects using the actual TLS implementations. |
| Padding bandwidth | Measure added bytes across realistic page loads and calibrate the experimental ranges before broad enablement. Keep `StartPadding` off until that evidence is accepted. |

The HTTP/3 post-header scheduler's parity rows must be run before release; the DATA-after-trailers
difference remains open alongside direct QUIC deployment checks.
A successful direct QUIC tunnel does not establish parity for anonymous requests. Automatic Alt-Svc remains off;
carrying the tunnel over standard HTTP/3 through web frontends remains future work.

## Rollout and documentation

1. Release and update servers first, keeping the minimum at its current default of 8 during app
   rollout. Verify the artifact's source commit and the deployed checks above.
2. Release the protocol-15 apps through the brand workflows. The 9.0.860 NuGet packages and server
   prerelease use the earlier host-bearing proof. Clients and servers using that shape must move
   together: neither mixed combination can authenticate. The server-first rolling upgrade applies to
   HTTP 8–14 clients; arrange a coordinated cutover for users already on the earlier protocol-15 shape.
3. Publish the minimum-protocol instructions on the IP-blocking page alongside the server release
   that enforces the setting before a body or upgrade. The MANAGER instructions belong in its server
   profile documentation; the server page links there. Use this release text:

   > Update your servers first, then your users' apps. Once all users have updated, set
   > `MinClientProtocolVersion` to `15` in the server's session options, or in its MANAGER server
   > profile. The server then refuses older clients before reading a request body or upgrading the
   > connection. Older apps cannot connect and receive no update message, so do not raise this
   > setting before they have updated. The server's default minimum remains `8` during rollout.
   >
   > If you already use the 9.0.860 protocol-15 server prerelease or apps built with its packages,
   > coordinate the server and app updates together: that earlier proof format cannot connect to
   > this one. The server-first rollout applies to apps still using HTTP versions 8–14.

4. Enable minimum 15 after the app rollout, then repeat the legacy probes on each actual route.
   Raising it early disconnects older apps. Lowering it restores their older unauthenticated entry
   path; it also gives up that part of the protection.
5. Treat `StartPadding` as a separate, default-off experiment. Enabling minimum 15 does not enable
   padding. Check deployment and mobile measurements before deciding where to enable it.

The release notes must describe measured behavior, without promising undetectability. Proof expiry
does not protect a permanent farm secret already seen by a TLS-terminating proxy through an access-key
update or redirect. See [the protocol's scope](protocol-15.md#scope-and-remaining-deployment-checks).
