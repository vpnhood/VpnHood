using System.Net.Quic;
using VpnHood.Test.KestrelParity.Probing;

namespace VpnHood.Test.KestrelParity;

public partial class Http3FrontTest
{
    [TestMethod]
    [TestCategory("Http3Framing")]
    [DataRow("long-body", QuicError.StreamAborted, 0x101L)]
    [DataRow("short-body-fin", QuicError.StreamAborted, 0x101L)]
    [DataRow("settings-after-headers", QuicError.ConnectionAborted, 0x105L)]
    [DataRow("settings-after-data", QuicError.ConnectionAborted, 0x105L)]
    [DataRow("settings-after-trailers", QuicError.ConnectionAborted, 0x105L)]
    [DataRow("headers-after-trailers", QuicError.ConnectionAborted, 0x105L)]
    [DataRow("uppercase-trailer-first", QuicError.StreamAborted, 0x10eL)]
    [DataRow("uppercase-trailer-ninth", QuicError.StreamAborted, 0x10eL)]
    [DataRow("qpack-trailer-prefix", QuicError.StreamAborted, 0x102L)]
    [DataRow("qpack-trailer-first", QuicError.StreamAborted, 0x102L)]
    [DataRow("qpack-trailer-ninth", QuicError.StreamAborted, 0x102L)]
    [DataRow("qpack-trailer-section-end", QuicError.StreamAborted, 0x102L)]
    public async Task Post_header_bursts_preserve_protocol_outcomes(string layout,
        QuicError error, long errorCode)
    {
        var name = "post-header-burst-" + layout;
        var bytes = BuildPostHeaderBurst(layout);
        if (error == QuicError.ConnectionAborted)
            await AssertFreshBurstOutcomes(name, bytes, errorCode, TestContext.CancellationToken);
        else
            await AssertReusedBurstOutcomes(name, bytes, errorCode, allowLimitResponse: false,
                TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Validation")]
    [DataRow("uppercase-trailer-first", 0x10eL)]
    [DataRow("uppercase-trailer-ninth", 0x10eL)]
    [DataRow("qpack-trailer-prefix", 0x102L)]
    [DataRow("qpack-trailer-first", 0x102L)]
    [DataRow("qpack-trailer-ninth", 0x102L)]
    [DataRow("qpack-trailer-section-end", 0x102L)]
    public async Task Malformed_trailers_during_a_held_application_reset_the_stream(string layout, long errorCode)
    {
        var headers = BuildLiteralRequest([.. ValidationHeaders(), ("x-parity-wait-for-abort", "1")]);
        var request = BuildPostHeaderBurst(layout, headers);
        // Both applications remain held until trailer parsing selects its error. Keep the client
        // write side open so neither FIN acknowledgement nor a response can hide the reset code.
        var expected = await ObserveRequestEnding(Servers.ReferenceEndPoint, request, finish: false,
            atomicFin: false, TestContext.CancellationToken);
        var actual = await ObserveTrailersDuringHeldApplication(headers, request[headers.Length..], [],
            TestContext.CancellationToken);
        var error = new Http3ProbeResult("", false, QuicError.StreamAborted, errorCode);

        Assert.AreEqual(error, expected.Response);
        Assert.AreEqual(error, expected.Write);
        AssertRequestEnding(layout + "-parser-first", expected, actual);
    }

    private static byte[] BuildPostHeaderBurst(string layout) =>
        BuildPostHeaderBurst(layout, BuildLiteralRequest(ValidationHeaders()));

    private static byte[] BuildPostHeaderBurst(string layout, byte[] headers)
    {
        IReadOnlyList<(string Name, string Value)> trailerFields = [.. Enumerable.Repeat(("x", ""), 8)];
        var trailers = BuildLiteralRequest(trailerFields);
        // RC3 Http3Stream checks DATA length, FIN, frame order, trailer callbacks and QPACK separately.
        // https://github.com/dotnet/aspnetcore/blob/v11.0.0-rc.3.26522.148/src/Servers/Kestrel/Core/src/Internal/Http3/Http3Stream.cs
        return layout switch {
            "long-body" => [.. BuildBodyHeaders("2"), .. Frame(0, [1, 2, 3])],
            "short-body-fin" => [.. BuildBodyHeaders("4"), .. Frame(0, [1, 2, 3])],
            "settings-after-headers" => [.. headers, .. Frame(4, [])],
            "settings-after-data" => [.. headers, .. Frame(0, [1, 2, 3]), .. Frame(4, [])],
            "settings-after-trailers" => [.. headers, .. trailers, .. Frame(4, [])],
            "headers-after-trailers" => [.. headers, .. trailers, .. BuildLiteralRequest([("x", "")])],
            "uppercase-trailer-first" => [.. headers, .. BuildLiteralRequest([("X", "")])],
            "uppercase-trailer-ninth" => [.. headers, .. BuildLiteralRequest([.. trailerFields, ("X", "")])],
            "qpack-trailer-prefix" => [.. headers, .. Frame(1, [2, 0])],
            "qpack-trailer-first" => [.. headers, .. BuildMalformedTrailerSection(0, [0x80])],
            "qpack-trailer-ninth" => [.. headers, .. BuildMalformedTrailerSection(8, [0x80])],
            // The frame is complete, but the literal's one-byte value is absent at the section boundary.
            "qpack-trailer-section-end" => [.. headers, .. BuildMalformedTrailerSection(0, [0x21, (byte)'x', 1])],
            _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, "Unknown post-header burst layout.")
        };
    }

    private static byte[] BuildMalformedTrailerSection(int validFields, IReadOnlyList<byte> malformedField)
    {
        var block = new List<byte> { 0, 0 };
        for (var i = 0; i < validFields; i++)
            block.AddRange([0x21, (byte)'x', 0]);
        // Keep the malformed representation inside the section, not in a second HEADERS frame.
        block.AddRange(malformedField);
        return Frame(1, block.ToArray());
    }
}
