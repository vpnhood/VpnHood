using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.ProbeResistance;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Net.Quic.MsQuic;
using VpnHood.Net.Toolkit.Streams;
using VpnHood.Test.KestrelParity.Probing;
using VpnHood.Test.KestrelParity.Servers;

namespace VpnHood.Test.KestrelParity;

// The reference is the installed Kestrel, not a second copy of our classifier. Every malformed probe must
// receive a result within its deadline; equal timeouts do not establish equivalent protocol behavior.
[TestClass]
public partial class Http3FrontTest
{
    private static Http3ParityServers? _servers;
    private static Http3ParityServers Servers => _servers ?? throw new InvalidOperationException("Servers did not start.");
    public required TestContext TestContext { get; set; }

    [ClassInitialize]
    public static async Task Initialize(TestContext context)
    {
        if (!QuicConnection.IsSupported || !MsQuicServer.IsSupported)
            Assert.Inconclusive("MsQuic is unavailable on this platform.");
        // This assembly has its own standalone fixture, alongside the HTTP/1 parity fixture's storage.
        var storagePath = Path.Combine(Path.GetTempPath(), "vh", "VpnHood.Test.KestrelParity", "http3",
            Guid.CreateVersion7().ToString());
        _servers = await Http3ParityServers.Start(storagePath, context.CancellationToken);
        context.WriteLine($"HTTP/3 reference: Kestrel {ReferenceServer.Version}; storage: {storagePath}");
        ThreadPool.GetMinThreads(out var workerThreads, out var completionThreads);
        context.WriteLine($"HTTP/3 measurement environment: processors={Environment.ProcessorCount}; " +
            $"minimum worker threads={workerThreads}; minimum completion threads={completionThreads}.");
    }

    [ClassCleanup]
    public static async Task Cleanup()
    {
        if (_servers != null)
            await _servers.DisposeAsync();
    }

    [TestMethod]
    [DataRow("GET")]
    [DataRow("POST")]
    public async Task Ordinary_http3_requests_receive_the_reference_challenge(string method)
    {
        using var referenceClient = CreateHttpClient();
        using var ourClient = CreateHttpClient();
        for (var i = 0; i < 2; i++) {
            using var expectedRequest = CreateHttp3Request(Servers.ReferenceEndPoint, method, $"/probe/{i}");
            using var actualRequest = CreateHttp3Request(Servers.OurEndPoint, method, $"/probe/{i}");
            using var expected = await referenceClient.SendAsync(expectedRequest, TestContext.CancellationToken);
            using var actual = await ourClient.SendAsync(actualRequest, TestContext.CancellationToken);
            Assert.AreEqual(HttpVersion.Version30, actual.Version);
            Assert.AreEqual(HttpStatusCode.Unauthorized, expected.StatusCode);
            Assert.AreEqual(expected.StatusCode, actual.StatusCode);
            Assert.AreEqual(expected.Headers.WwwAuthenticate.ToString(), actual.Headers.WwwAuthenticate.ToString());
            Assert.AreEqual(expected.Headers.Server.ToString(), actual.Headers.Server.ToString());
            Assert.AreEqual(expected.Content.Headers.ContentLength, actual.Content.Headers.ContentLength);
            Assert.IsNotNull(actual.Headers.Date);
            Assert.IsTrue((DateTimeOffset.UtcNow - actual.Headers.Date.Value).Duration() < TimeSpan.FromSeconds(5));
            Assert.IsEmpty(await actual.Content.ReadAsByteArrayAsync(TestContext.CancellationToken));
        }
    }

    [TestMethod]
    public async Task Server_control_settings_match_the_reference()
    {
        var expected = await ReadSettings(Servers.ReferenceEndPoint, TestContext.CancellationToken);
        var actual = await ReadSettings(Servers.OurEndPoint, TestContext.CancellationToken);
        CollectionAssert.AreEqual(expected, actual);
        // Capacity and blocked-stream settings may be omitted, which means zero. Assert the effective
        // values as well as the wire comparison: requests must never require a dynamic table allocation.
        using var settings = new MemoryStream(actual);
        var capacity = 0UL;
        var blockedStreams = 0UL;
        while (settings.Position < settings.Length) {
            var id = await ReadVariableInteger(settings, TestContext.CancellationToken);
            var value = await ReadVariableInteger(settings, TestContext.CancellationToken);
            if (id == 1)
                capacity = value;
            if (id == 7)
                blockedStreams = value;
        }
        Assert.AreEqual(0UL, capacity, "The front must advertise zero dynamic QPACK capacity.");
        Assert.AreEqual(0UL, blockedStreams, "The front must not advertise blocked QPACK requests.");
    }

    [TestMethod]
    [DataRow("missing-settings", new byte[] { 0, 0x21, 0 }, false, false)]
    [DataRow("data-on-control", new byte[] { 0, 0, 0 }, false, false)]
    [DataRow("duplicate-settings", new byte[] { 0, 4, 0, 4, 0 }, false, false)]
    [DataRow("forbidden-setting", new byte[] { 0, 4, 2, 2, 1 }, false, false)]
    [DataRow("duplicate-control", new byte[] { 0, 4, 0 }, true, false)]
    [DataRow("closed-control", new byte[] { 0, 4, 0 }, false, true)]
    [DataRow("closed-qpack-decoder", new byte[] { 3 }, false, true)]
    [DataRow("closed-qpack-encoder", new byte[] { 2 }, false, true)]
    [DataRow("duplicate-qpack-encoder", new byte[] { 2 }, true, false)]
    [DataRow("duplicate-qpack-decoder", new byte[] { 3 }, true, false)]
    [DataRow("http2-max-concurrent-streams-setting", new byte[] { 0, 4, 2, 3, 1 }, false, false)]
    [DataRow("http2-initial-window-setting", new byte[] { 0, 4, 2, 4, 1 }, false, false)]
    [DataRow("http2-max-frame-size-setting", new byte[] { 0, 4, 2, 5, 1 }, false, false)]
    [DataRow("unknown-control-frame-then-data", new byte[] { 0, 4, 0, 0x21, 2, 7, 8, 0, 0 }, false, false)]
    public async Task Malformed_control_streams_match_the_reference(string name, byte[] bytes, bool duplicate,
        bool finish)
    {
        var probe = CreateProbe();
        var expected = await probe.Control(Servers.ReferenceEndPoint, bytes, duplicate, finish, TestContext.CancellationToken);
        var actual = await probe.Control(Servers.OurEndPoint, bytes, duplicate, finish, TestContext.CancellationToken);
        Assert.AreEqual(QuicError.ConnectionAborted, expected.Error, $"{name}: the reference must close the connection.");
        AssertSame(name, expected, actual);
    }

    [TestMethod]
    public async Task Duplicate_setting_ids_leave_a_normal_request_usable_like_the_reference()
    {
        // The measured reference accepts the repeated setting inside one SETTINGS frame. Require a
        // complete response, rather than treating its open control stream and silence as a failure.
        await AssertResponseMatches("duplicate-setting-id", BuildQpackRequest([]), [], null,
            TestContext.CancellationToken, initialControlBytes: [0, 4, 4, 1, 0, 1, 0]);
    }

    [TestMethod]
    [DataRow("data-before-headers", new byte[] { 0, 0 })]
    [DataRow("settings-on-request", new byte[] { 4, 0 })]
    [DataRow("invalid-qpack", new byte[] { 1, 3, 0, 0, 0xff })]
    [DataRow("truncated-varint", new byte[] { 0x80 })]
    [DataRow("truncated-headers", new byte[] { 1, 5, 0 })]
    public async Task Malformed_request_streams_match_the_reference(string name, byte[] bytes)
    {
        await AssertRequestMatches(name, bytes, fragmented: false, withholdFin: false, sendFin: true,
            TestContext.CancellationToken);
    }

    [TestMethod]
    [DataRow("nonzero-required-insert-count", new byte[] { 2, 0, 0xd1 })]
    [DataRow("dynamic-index", new byte[] { 0, 0, 0x80 })]
    [DataRow("post-base-index", new byte[] { 0, 0, 0x10 })]
    [DataRow("static-index-out-of-range", new byte[] { 0, 0, 0xff, 0x24 })]
    [DataRow("truncated-literal-value", new byte[] { 0, 0, 0x21, 0x61, 2, 0x61 })]
    [DataRow("invalid-huffman-padding", new byte[] { 0, 0, 0x21, 0x61, 0x81, 0xff })]
    [DataRow("huffman-eos-in-value", new byte[] { 0, 0, 0x21, 0x61, 0x84, 0xff, 0xff, 0xff, 0xff })]
    public async Task Invalid_qpack_has_a_bounded_protocol_result(string name, byte[] headerBlock)
    {
        await AssertRequestMatches(name, Frame(1, headerBlock), fragmented: true, withholdFin: false,
            sendFin: true, TestContext.CancellationToken);
    }

    [TestMethod]
    [DataRow("uppercase-name", new byte[] { 0x21, 0x41, 0 })]
    [DataRow("duplicate-method", new byte[] { 0xd1 })]
    [DataRow("pseudo-header-after-field", new byte[] { 0x21, 0x61, 0, 0xc1 })]
    [DataRow("connection-specific-field", new byte[] { 0x27, 3, 0x63, 0x6f, 0x6e, 0x6e, 0x65, 0x63, 0x74, 0x69, 0x6f, 0x6e, 0 })]
    public async Task Invalid_decoded_fields_match_the_reference(string name, byte[] fields)
    {
        await AssertRequestMatches(name, BuildQpackRequest(fields), fragmented: false, withholdFin: false,
            sendFin: true, TestContext.CancellationToken);
    }

    [TestMethod]
    [DataRow("connect-empty-port")]
    [DataRow("connect-invalid-port")]
    [DataRow("authority-missing-bracket")]
    [DataRow("authority-extra-after-bracket")]
    [DataRow("authority-invalid-port")]
    [DataRow("authority-empty-port")]
    [DataRow("empty-path")]
    [DataRow("non-options-asterisk")]
    [DataRow("duplicate-content-length")]
    [DataRow("missing-method")]
    public async Task Invalid_request_header_semantics_match_the_reference(string name)
    {
        List<(string Name, string Value)> fields = [
            (":method", "GET"), (":scheme", "https"), (":path", "/"), (":authority", Servers.HostName)
        ];
        switch (name) {
            case "connect-empty-port": fields = [(":method", "CONNECT"), (":authority", Servers.HostName + ":")]; break;
            case "connect-invalid-port": fields = [(":method", "CONNECT"), (":authority", Servers.HostName + ":abc")]; break;
            case "authority-missing-bracket": fields[3] = (":authority", "[::1"); break;
            case "authority-extra-after-bracket": fields[3] = (":authority", "[::1]extra"); break;
            case "authority-invalid-port": fields[3] = (":authority", Servers.HostName + ":abc"); break;
            case "authority-empty-port": fields[3] = (":authority", Servers.HostName + ":"); break;
            case "empty-path": fields[2] = (":path", ""); break;
            case "non-options-asterisk": fields[2] = (":path", "*"); break;
            case "duplicate-content-length":
                fields.Add(("content-length", "0"));
                fields.Add(("content-length", "0"));
                break;
            case "missing-method": fields.RemoveAt(0); break;
            default: throw new ArgumentOutOfRangeException(nameof(name));
        }
        // Record both the numeric error and its stream/connection scope. A timeout never passes.
        await AssertRequestMatches(name, BuildLiteralRequest(fields), fragmented: false, withholdFin: false,
            sendFin: true, TestContext.CancellationToken);
    }

    [TestMethod]
    [DataRow("matching-authority-and-host")]
    [DataRow("host-without-authority")]
    [DataRow("ipv6-authority")]
    [DataRow("connect-with-port")]
    [DataRow("connect-ipv6-with-port")]
    [DataRow("options-asterisk")]
    [DataRow("authority-host-mismatch")]
    [DataRow("empty-host-with-authority")]
    [DataRow("empty-authority-with-host")]
    [DataRow("connect-host-without-authority")]
    [DataRow("connect-missing-port")]
    [DataRow("field-leading-space")]
    [DataRow("field-trailing-tab")]
    [DataRow("duplicate-host")]
    public async Task Header_forms_accepted_by_the_reference_receive_its_challenge(string name)
    {
        List<(string Name, string Value)> fields = [
            (":method", "GET"), (":scheme", "https"), (":path", "/"), (":authority", Servers.HostName)
        ];
        switch (name) {
            case "authority-host-mismatch": fields.Add(("host", "other.example.com")); break;
            case "empty-host-with-authority": fields.Add(("host", "")); break;
            case "empty-authority-with-host":
                fields[3] = (":authority", "");
                fields.Add(("host", Servers.HostName));
                break;
            case "connect-host-without-authority":
                fields = [(":method", "CONNECT"), ("host", Servers.HostName + ":443")];
                break;
            case "connect-missing-port": fields = [(":method", "CONNECT"), (":authority", Servers.HostName)]; break;
            case "field-leading-space": fields.Add(("x-test", " leading")); break;
            case "field-trailing-tab": fields.Add(("x-test", "trailing\t")); break;
            case "duplicate-host":
                fields.Add(("host", Servers.HostName));
                fields.Add(("host", Servers.HostName));
                break;
            case "matching-authority-and-host": fields.Add(("host", Servers.HostName)); break;
            case "host-without-authority": fields[3] = ("host", Servers.HostName); break;
            case "ipv6-authority": fields[3] = (":authority", "[::1]:443"); break;
            case "connect-with-port": fields = [(":method", "CONNECT"), (":authority", Servers.HostName + ":443")]; break;
            case "connect-ipv6-with-port": fields = [(":method", "CONNECT"), (":authority", "[::1]:443")]; break;
            case "options-asterisk":
                fields[0] = (":method", "OPTIONS");
                fields[2] = (":path", "*");
                break;
            default: throw new ArgumentOutOfRangeException(nameof(name));
        }
        await AssertResponseMatches(name, BuildLiteralRequest(fields), [], null, TestContext.CancellationToken);
    }

    [TestMethod]
    [DataRow("static-fields", new byte[] { })]
    [DataRow("literal-field", new byte[] { 0x21, 0x61, 3, 0x61, 0x62, 0x63 })]
    [DataRow("huffman-value", new byte[] { 0x21, 0x61, 0x8c, 0xf1, 0xe3, 0xc2, 0xe5, 0xf2, 0x3a, 0x6b, 0xa0, 0xab, 0x90, 0xf4, 0xff })]
    public async Task Static_literal_and_huffman_qpack_requests_receive_the_reference_response(string name,
        byte[] fields)
    {
        // The Huffman vector encodes www.example.com from RFC 7541, appendix C.4.1. These bytes are
        // independent of the production encoder/decoder; sharing them would hide matching codec bugs.
        await AssertResponseMatches(name, BuildQpackRequest(fields), [], null, TestContext.CancellationToken);
    }

    [TestMethod]
    [DataRow(100)]
    [DataRow(101)]
    [DataRow(200)]
    [DataRow(201)]
    public async Task Header_count_boundaries_match_the_reference(int totalCount)
    {
        List<(string Name, string Value)> fields = [
            (":method", "GET"), (":scheme", "https"), (":path", "/"), (":authority", Servers.HostName)
        ];
        for (var i = fields.Count; i < totalCount; i++)
            fields.Add(("x-test", "a"));
        await AssertRequestMatches($"header-count-{totalCount}", BuildLiteralRequest(fields), fragmented: false,
            withholdFin: false, sendFin: true, TestContext.CancellationToken);
    }

    [TestMethod]
    [DataRow("large-literal-value")]
    [DataRow("huffman-expansion")]
    [DataRow("incomplete-large-frame")]
    [DataRow("invalid-large-qpack")]
    public async Task Header_size_and_invalid_large_frame_boundaries_match_the_reference(string name)
    {
        byte[] request;
        switch (name) {
            case "large-literal-value":
                request = BuildLiteralRequest([
                    (":method", "GET"), (":scheme", "https"), (":path", "/"), (":authority", Servers.HostName),
                    ("x-test", new string('a', 32769))
                ]);
                break;
            case "huffman-expansion":
                // RFC 7541's five zero bits encode '0'. This valid 20 KB compressed value expands
                // beyond 32 KB without depending on the production Huffman encoder or decoder.
                const int decodedLength = 32769;
                var encoded = new byte[(decodedLength * 5 + 7) / 8];
                var paddingBits = encoded.Length * 8 - decodedLength * 5;
                encoded[^1] = (byte)((1 << paddingBits) - 1);
                using (var field = new MemoryStream()) {
                    field.Write(new byte[] { 0x21, (byte)'x' });
                    WriteQpackInteger(field, 0x80, 7, encoded.Length);
                    field.Write(encoded);
                    request = BuildQpackRequest(field.ToArray());
                }
                break;
            case "incomplete-large-frame":
                using (var frame = new MemoryStream()) {
                    WriteVariableInteger(frame, 1);
                    WriteVariableInteger(frame, 1024 * 1024);
                    frame.Write(new byte[] { 0, 0, 0xd1 });
                    request = frame.ToArray();
                }
                break;
            case "invalid-large-qpack":
                // The first dynamic reference is invalid, regardless of the following padding bytes.
                request = Frame(1, new byte[32769]);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(name));
        }
        await AssertRequestMatches(name, request, fragmented: false, withholdFin: false, sendFin: true,
            TestContext.CancellationToken);
    }

    [TestMethod]
    [DataRow("complete-field-before-fin", new byte[] { 0, 0, 0xd1 })]
    [DataRow("partial-field-prefix-before-fin", new byte[] { 0 })]
    [DataRow("invalid-dynamic-reference", new byte[] { 0, 0, 0 })]
    [DataRow("duplicate-method-before-fin", new byte[] { 0, 0, 0xd1, 0xd1 })]
    [DataRow("uppercase-name-before-fin", new byte[] { 0, 0, 0xd1, 0x21, 0x41, 0 })]
    public async Task Fragmented_oversized_header_declarations_match_the_reference(string name, byte[] prefix)
    {
        using var frame = new MemoryStream();
        WriteVariableInteger(frame, 1);
        WriteVariableInteger(frame, 1024 * 1024);
        frame.Write(prefix);
        // Only a few bytes arrive despite the 1 MB declaration. Both sides must reach the observed
        // FIN/error result promptly, rather than buffering the declaration or waiting for its full size.
        await AssertRequestMatches(name, frame.ToArray(), fragmented: true, withholdFin: false, sendFin: true,
            TestContext.CancellationToken);
    }

    [TestMethod]
    [DataRow("before-request-headers")]
    [DataRow("after-request-headers")]
    [DataRow("on-control-stream")]
    [DataRow("unknown-one-way-stream")]
    public async Task Unknown_frames_and_streams_leave_a_normal_request_usable(string location)
    {
        byte[] unknown = [0x21, 3, 7, 8, 9];
        var request = BuildQpackRequest([]);
        if (location == "before-request-headers")
            request = [.. unknown, .. request];
        if (location == "after-request-headers")
            request = [.. request, .. unknown];
        var controlSuffix = location == "on-control-stream" ? unknown : [];
        byte[]? extraOneWay = location == "unknown-one-way-stream" ? [0x21, 7, 8, 9] : null;
        // A complete challenge and FIN establish progress. Silence on an ignored stream is not a pass.
        await AssertResponseMatches(location, request, controlSuffix, extraOneWay, TestContext.CancellationToken);
    }

    [TestMethod]
    [DataRow("GET")]
    [DataRow("POST")]
    public async Task Http3_headers_containing_a_valid_proof_do_not_enter_the_native_protocol(string method)
    {
        using var client = CreateHttpClient();
        using var request = CreateHttp3Request(Servers.OurEndPoint, method, "/probe");
        request.Headers.TryAddWithoutValidation("X-ProtocolVersion", "15");
        request.Headers.TryAddWithoutValidation("Authorization",
            "Bearer " + new RequestProof(Servers.FarmSecret).Create(15, DateTimeOffset.UtcNow));
        using var response = await client.SendAsync(request, TestContext.CancellationToken);
        Assert.AreEqual(HttpVersion.Version30, response.Version);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual("Bearer", response.Headers.WwwAuthenticate.ToString());
        Assert.IsEmpty(await response.Content.ReadAsByteArrayAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    [DataRow("GET")]
    [DataRow("POST")]
    public async Task A_signed_raw_request_inside_an_unknown_frame_cannot_bypass_http3(string method)
    {
        var unknown = Frame(0x21, Encoding.ASCII.GetBytes(BuildHead(method, "valid")));
        // The native signature occurs inside a frame payload, not at this request stream's beginning.
        // DATA before any HEADERS then supplies a deterministic protocol result from the reference.
        await AssertRequestMatches($"embedded-{method}", [.. unknown, 0, 0], fragmented: true,
            withholdFin: false, sendFin: true, TestContext.CancellationToken);
    }

    [TestMethod]
    [DataRow("GET", "missing")]
    [DataRow("POST", "missing")]
    [DataRow("GET", "malformed")]
    [DataRow("POST", "malformed")]
    [DataRow("GET", "wrong-key")]
    [DataRow("POST", "wrong-key")]
    [DataRow("GET", "expired")]
    [DataRow("POST", "expired")]
    [DataRow("GET", "legacy")]
    [DataRow("POST", "legacy")]
    [DataRow("GET", "signed-legacy")]
    [DataRow("POST", "signed-legacy")]
    public async Task Raw_http1_without_a_valid_proof_is_not_a_second_public_protocol(string method, string failure)
    {
        var bytes = Encoding.ASCII.GetBytes(BuildHead(method, failure));
        await AssertRequestMatches($"{method}-{failure}", bytes, fragmented: false, withholdFin: false,
            sendFin: true, TestContext.CancellationToken);
    }

    [TestMethod]
    [DataRow("GET")]
    [DataRow("POST")]
    public async Task Fragmented_raw_headers_and_a_withheld_body_match_the_reference(string method)
    {
        var bytes = Encoding.ASCII.GetBytes(BuildHead(method, "expired"));
        await AssertRequestMatches($"{method}-fragmented-withheld", bytes, fragmented: true, withholdFin: true,
            sendFin: true, TestContext.CancellationToken);
    }

    [TestMethod]
    [DataRow("GET", 5152)]
    [DataRow("POST", 4948)]
    public async Task An_unfinished_raw_header_does_not_delay_an_http3_frame_error(string method, int frameLength)
    {
        // GET and POST also begin valid QUIC varints: an unknown frame with this payload length. DATA after
        // that frame is invalid before HEADERS. Waiting for HTTP/1's CRLFCRLF would miss Kestrel's early error.
        var prefix = Encoding.ASCII.GetBytes($"{method} / HTTP/1.1\r\nHost: {Servers.HostName}\r\nX-Probe: ");
        var bytes = new byte[4 + frameLength + 2];
        Array.Fill(bytes, (byte)'a');
        prefix.CopyTo(bytes, 0);
        bytes[^2] = 0;
        bytes[^1] = 0;
        await AssertRequestMatches($"{method}-unfinished-large-header", bytes, fragmented: false,
            withholdFin: false, sendFin: false, TestContext.CancellationToken);
    }

    [TestMethod]
    [DataRow("GET")]
    [DataRow("POST")]
    public async Task Large_raw_headers_do_not_bypass_the_http3_parser(string method)
    {
        var head = BuildHead(method, "wrong-key").Replace("\r\n\r\n",
            "\r\nX-Probe: " + new string('a', 12000) + "\r\n\r\n", StringComparison.Ordinal);
        await AssertRequestMatches($"{method}-large-header", Encoding.ASCII.GetBytes(head), fragmented: true,
            withholdFin: false, sendFin: true, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Slow")]
    public async Task A_partial_raw_get_keeps_kestrels_request_header_timeout()
    {
        var headerTimeout = new KestrelServerOptions().Limits.RequestHeadersTimeout;
        var probe = CreateProbe();
        // Run the two real default timeouts side by side. A test-only short timeout would not establish that
        // the production gate preserves the reference's normal timeout; this pair costs one timeout, not two.
        var results = await Task.WhenAll(
            probe.PartialRequest(Servers.ReferenceEndPoint, headerTimeout, TestContext.CancellationToken),
            probe.PartialRequest(Servers.OurEndPoint, headerTimeout, TestContext.CancellationToken));
        AssertSame("partial-GET-header-timeout", results[0].Result, results[1].Result);
        TestContext.WriteLine($"Header timeout {headerTimeout}; reference {results[0].Elapsed}; ours {results[1].Elapsed}.");
        foreach (var result in results) {
            Assert.IsTrue(result.Elapsed >= headerTimeout - TimeSpan.FromSeconds(1), "The request ended too early.");
            Assert.IsTrue(result.Elapsed <= headerTimeout + TimeSpan.FromSeconds(5), "The header timeout was delayed.");
        }
        // Kestrel checks timeouts on a one-second heartbeat. Allow one further tick for local scheduling.
        Assert.IsTrue((results[0].Elapsed - results[1].Elapsed).Duration() <= TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task Signed_native_requests_preserve_the_body_and_need_no_server_stream_credit()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        deadline.CancelAfter(Http3Probe.Timeout);
        await using var connection = await CreateProbe().Connect(Servers.OurEndPoint, 0, deadline.Token);
        // The desktop native client grants no inbound one-way streams. Both requests must finish even if
        // opening the server's control stream is pending; the second proves the connection remains usable.
        await SendSignedStatus(connection, fragmented: false, deadline.Token);
        await SendSignedStatus(connection, fragmented: true, deadline.Token);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_signed_native_upgrade_still_returns_101(bool rewriteHost)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        deadline.CancelAfter(Http3Probe.Timeout);
        await using var connection = await CreateProbe().Connect(Servers.OurEndPoint, 0, deadline.Token);
        await using var stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, deadline.Token);
        var head = BuildHead("GET", "valid", hostName: rewriteHost ? "origin.example.com" : Servers.HostName);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), deadline.Token);
        using var response = await HttpUtils.ReadResponse(stream, deadline.Token);
        Assert.AreEqual(HttpStatusCode.SwitchingProtocols, response.StatusCode);
        Assert.AreEqual("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", response.Headers.GetValues("Sec-WebSocket-Accept").Single());
    }

    [TestMethod]
    public async Task A_valid_stream_does_not_authenticate_a_later_stream()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        deadline.CancelAfter(Http3Probe.Timeout);
        var probe = CreateProbe();
        await using var ours = await probe.Connect(Servers.OurEndPoint, 0, deadline.Token);
        await SendSignedStatus(ours, fragmented: false, deadline.Token);
        await using var reference = await probe.Connect(Servers.ReferenceEndPoint, 0, deadline.Token);
        await using var ourRequest = await ours.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, deadline.Token);
        await using var referenceRequest = await reference.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, deadline.Token);
        var bytes = Encoding.ASCII.GetBytes(BuildHead("GET", "missing"));
        var expected = await Http3Probe.Request(referenceRequest, bytes, false, false, true, deadline.Token);
        var actual = await Http3Probe.Request(ourRequest, bytes, false, false, true, deadline.Token);
        AssertSame("unauthenticated-second-stream", expected, actual);
    }

    private async Task AssertRequestMatches(string name, byte[] bytes, bool fragmented, bool withholdFin, bool sendFin,
        CancellationToken cancellationToken)
    {
        var probe = CreateProbe();
        var expected = await probe.Request(Servers.ReferenceEndPoint, bytes, fragmented, withholdFin, sendFin, cancellationToken);
        var actual = await probe.Request(Servers.OurEndPoint, bytes, fragmented, withholdFin, sendFin, cancellationToken);
        AssertSame(name, expected, actual);
    }

    private static async Task AssertResponseMatches(string name, byte[] request, byte[] controlSuffix,
        byte[]? extraOneWay, CancellationToken cancellationToken, byte[]? initialControlBytes = null)
    {
        var probe = CreateProbe();
        var expected = await probe.Response(Servers.ReferenceEndPoint, request, controlSuffix, extraOneWay,
            initialControlBytes, cancellationToken);
        var actual = await probe.Response(Servers.OurEndPoint, request, controlSuffix, extraOneWay,
            initialControlBytes, cancellationToken);
        Assert.IsNotEmpty(expected, $"{name}: the reference must send its challenge before FIN.");
        Assert.AreEqual((byte)1, expected[0], $"{name}: the response must start with a HEADERS frame.");
        Assert.AreEqual(NormalizeResponseDate(expected), NormalizeResponseDate(actual), name);
    }

    private static string NormalizeResponseDate(byte[] bytes)
    {
        // Kestrel's challenge uses literal response values. Compare complete frame bytes, independent of
        // transport chunk boundaries, allowing only the live Date value to change between the servers.
        var response = Encoding.Latin1.GetString(bytes);
        const string datePattern = @"(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun), \d{2} [A-Za-z]{3} \d{4} \d{2}:\d{2}:\d{2} GMT";
        Assert.HasCount(1, Regex.Matches(response, datePattern), "The challenge must contain one literal Date.");
        return Regex.Replace(response, datePattern, "<date>");
    }

    private void AssertSame(string name, Http3ProbeResult expected, Http3ProbeResult actual)
    {
        TestContext.WriteLine($"{name}: reference={expected}; ours={actual}");
        Assert.IsFalse(actual.Bytes.StartsWith("485454502F", StringComparison.Ordinal),
            $"{name}: an unauthenticated h3 stream received an HTTP/1 response.");
        if (expected.Bytes.Length > 0 && actual.Bytes.Length > 0) {
            // Limit probes can yield an HTTP error response instead of a reset. Preserve every
            // encoded byte except the live Date, and still compare FIN and error scope separately.
            expected = expected with { Bytes = NormalizeResponseDate(Convert.FromHexString(expected.Bytes)) };
            actual = actual with { Bytes = NormalizeResponseDate(Convert.FromHexString(actual.Bytes)) };
        }
        Assert.AreEqual(expected, actual, name);
    }

    private static Http3Probe CreateProbe() => new(Servers.HostName, Servers.Certificate);

    private static HttpClient CreateHttpClient() => new(new SocketsHttpHandler {
        UseProxy = false,
        SslOptions = new SslClientAuthenticationOptions {
            RemoteCertificateValidationCallback = (_, presented, _, _) =>
                presented?.GetCertHashString() == Servers.Certificate.GetCertHashString()
        },
        ConnectTimeout = Http3Probe.Timeout
    }) { Timeout = Http3Probe.Timeout };

    private static HttpRequestMessage CreateHttp3Request(IPEndPoint endPoint, string method, string path) =>
        // Pin the loopback address and send the same authority/SNI as our raw probes. The production
        // certificate selector need not recognize localhost, and localhost can resolve to another address.
        new(new HttpMethod(method), $"https://127.0.0.1:{endPoint.Port}{path}") {
            Version = HttpVersion.Version30,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Headers = { Host = Servers.HostName }
        };

    private static string BuildHead(string method, string proofKind, int contentLength = 1024, string? hostName = null)
    {
        var proof = new RequestProof(Servers.FarmSecret);
        var now = DateTimeOffset.UtcNow;
        var token = proofKind switch {
            "missing" or "legacy" => null,
            "malformed" => "not-a-jwt",
            "wrong-key" => new RequestProof(RandomNumberGenerator.GetBytes(16)).Create(15, now),
            "expired" => proof.Create(15, now.AddHours(-1)),
            // The signer only creates current proofs. Put that valid proof on a legacy-version head
            // to exercise the listener's version gate independently of the signature's authenticity.
            "signed-legacy" => proof.Create(15, now),
            "valid" => proof.Create(15, now),
            _ => throw new ArgumentOutOfRangeException(nameof(proofKind))
        };
        var authorization = token == null ? "" : $"Authorization: Bearer {token}\r\n";
        var version = proofKind is "legacy" or "signed-legacy" ? 14 : 15;
        var framing = method == "GET"
            ? "Connection: Upgrade\r\nUpgrade: websocket\r\nSec-WebSocket-Version: 13\r\n" +
              "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n"
            : $"X-BinaryStream: None\r\nContent-Length: {contentLength}\r\n";
        return $"{method} /probe HTTP/1.1\r\nHost: {hostName ?? Servers.HostName}\r\nX-ProtocolVersion: {version}\r\n" +
               authorization + framing + "\r\n";
    }

    private static async Task SendSignedStatus(QuicConnection connection, bool fragmented,
        CancellationToken cancellationToken)
    {
        var status = new SessionStatusRequest {
            RequestId = Guid.NewGuid().ToString(),
            SessionId = ulong.MaxValue,
            SessionKey = new byte[16]
        };
        var payload = StreamUtils.ObjectToJsonBuffer(status);
        var head = Encoding.ASCII.GetBytes(BuildHead("POST", "valid", payload.Length + 2));
        byte[] bytes = [.. head, 1, status.RequestCode, .. payload.Span];
        await using var stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cancellationToken);
        if (fragmented) {
            await stream.WriteAsync(bytes.AsMemory(0, 2), cancellationToken);
            await stream.WriteAsync(bytes.AsMemory(2), cancellationToken);
        }
        else {
            // One write deliberately coalesces the proof head and request body, exercising prefix replay.
            await stream.WriteAsync(bytes, cancellationToken);
        }
        stream.CompleteWrites();
        using var response = await HttpUtils.ReadResponse(stream, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await StreamUtils.ReadMessageAsync(stream, cancellationToken);
        var session = JsonSerializer.Deserialize<SessionResponse>(json);
        Assert.IsNotNull(session);
        Assert.AreEqual(SessionErrorCode.AccessError, session.ErrorCode);
        Assert.AreEqual("Invalid SessionId.", session.ErrorMessage);
    }

    private static byte[] BuildQpackRequest(byte[] extraFields)
    {
        // Required Insert Count=0, Delta Base=0; static GET, https, /; literal :authority value.
        // Deliberately use a tiny independent encoder for this fixed request, never the front's codec.
        var authority = Encoding.ASCII.GetBytes(Servers.HostName);
        Assert.IsTrue(authority.Length < 127, "The fixture's authority should fit one QPACK length byte.");
        byte[] block = [0, 0, 0xd1, 0xd7, 0xc1, 0x50, (byte)authority.Length, .. authority, .. extraFields];
        return Frame(1, block);
    }

    private static byte[] BuildLiteralRequest(IEnumerable<(string Name, string Value)> fields)
    {
        // The semantic and size probes use literal fields. Keep this small encoder independent of
        // production; only the value-length integer needs to grow for the header-limit cases.
        using var block = new MemoryStream();
        block.Write(new byte[] { 0, 0 });
        foreach (var (name, value) in fields) {
            var nameBytes = Encoding.ASCII.GetBytes(name);
            var valueBytes = Encoding.ASCII.GetBytes(value);
            Assert.IsTrue(nameBytes.Length < 134 && valueBytes.Length <= 65536);
            WriteQpackInteger(block, 0x20, 3, nameBytes.Length);
            block.Write(nameBytes);
            WriteQpackInteger(block, 0, 7, valueBytes.Length);
            block.Write(valueBytes);
        }
        return Frame(1, block.ToArray());
    }

    private static void WriteQpackInteger(Stream stream, byte prefix, int prefixBits, int value)
    {
        var mask = (1 << prefixBits) - 1;
        stream.WriteByte((byte)(prefix | Math.Min(value, mask)));
        if (value < mask)
            return;
        value -= mask;
        while (value >= 128) {
            stream.WriteByte((byte)(0x80 | (value & 0x7f)));
            value >>= 7;
        }
        stream.WriteByte((byte)value);
    }

    private static byte[] Frame(ulong type, byte[] payload)
    {
        using var frame = new MemoryStream();
        WriteVariableInteger(frame, type);
        WriteVariableInteger(frame, (ulong)payload.Length);
        frame.Write(payload);
        return frame.ToArray();
    }

    private static void WriteVariableInteger(Stream stream, ulong value)
    {
        Assert.IsTrue(value < 1073741824, "These bounded test frames need at most four-byte integers.");
        if (value >= 16384) {
            stream.WriteByte((byte)(0x80 | (value >> 24)));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
        }
        else if (value >= 64) {
            stream.WriteByte((byte)(0x40 | (value >> 8)));
        }
        stream.WriteByte((byte)value);
    }

    private static async Task<byte[]> ReadSettings(IPEndPoint endPoint, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Http3Probe.Timeout);
        await using var connection = await CreateProbe().Connect(endPoint, 3, deadline.Token);
        await using var control = await connection.AcceptInboundStreamAsync(deadline.Token);
        Assert.AreEqual(0UL, await ReadVariableInteger(control, deadline.Token), "Server stream is not a control stream.");
        Assert.AreEqual(4UL, await ReadVariableInteger(control, deadline.Token), "SETTINGS must be its first frame.");
        var length = await ReadVariableInteger(control, deadline.Token);
        Assert.IsTrue(length <= 1024, "Unexpectedly large reference SETTINGS.");
        var bytes = new byte[(int)length];
        await control.ReadExactlyAsync(bytes, deadline.Token);
        return bytes;
    }

    private static async Task<ulong> ReadVariableInteger(Stream stream, CancellationToken cancellationToken)
    {
        var first = new byte[1];
        await stream.ReadExactlyAsync(first, cancellationToken);
        var length = 1 << (first[0] >> 6);
        var remaining = new byte[length - 1];
        await stream.ReadExactlyAsync(remaining, cancellationToken);
        var value = (ulong)(first[0] & 0x3f);
        foreach (var item in remaining)
            value = (value << 8) | item;
        return value;
    }
}
