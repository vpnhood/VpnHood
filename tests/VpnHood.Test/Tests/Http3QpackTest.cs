using System.Globalization;
using System.Text;
using VpnHood.Net.Http3;

namespace VpnHood.Test.Tests;

[TestClass]
public class Http3QpackTest
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task Rfc9204_literal_name_reference_example_decodes()
    {
        // RFC 9204 Appendix B.1: :path = /index.html, without a dynamic table.
        var headers = await ReadHeaders(Convert.FromHexString("0000510b2f696e6465782e68746d6c"), TestContext.CancellationToken);
        CollectionAssert.AreEqual(new[] { new KeyValuePair<string, string>(":path", "/index.html") },
            headers.ToArray());
    }

    [TestMethod]
    public async Task A_browser_request_can_mix_static_fields_and_huffman_authority()
    {
        // The authority's Huffman bytes are RFC 7541 Appendix C.4.1's www.example.com vector.
        var headers = await ReadHeaders(Convert.FromHexString("0000d1d7c1508cf1e3c2e5f23a6ba0ab90f4ff"), TestContext.CancellationToken);
        CollectionAssert.AreEqual(new[] {
            new KeyValuePair<string, string>(":method", "GET"),
            new KeyValuePair<string, string>(":scheme", "https"),
            new KeyValuePair<string, string>(":path", "/"),
            new KeyValuePair<string, string>(":authority", "www.example.com")
        }, headers.ToArray());
    }

    [TestMethod]
    [DataRow("2f")]
    [DataRow("3f")]
    public async Task Huffman_literal_names_and_never_indexed_flags_decode(string first)
    {
        // RFC 7541 C.4.3's custom-key and custom-value strings, in QPACK's literal-name form.
        var headers = await ReadHeaders(Convert.FromHexString(
            "0000" + first + "0125a849e95ba97d7f8925a849e95bb8e8b4bf"), TestContext.CancellationToken);
        Assert.HasCount(1, headers);
        Assert.AreEqual("custom-key", headers[0].Key);
        Assert.AreEqual("custom-value", headers[0].Value);
    }

    [TestMethod]
    public async Task Header_order_duplicates_and_opaque_octets_are_preserved()
    {
        var headers = await ReadHeaders(Convert.FromHexString("0000217801613178016221780180"), TestContext.CancellationToken);
        Assert.HasCount(3, headers);
        Assert.AreEqual(new KeyValuePair<string, string>("x", "a"), headers[0]);
        Assert.AreEqual(new KeyValuePair<string, string>("x", "b"), headers[1]);
        Assert.AreEqual(new KeyValuePair<string, string>("x", "\u0080"), headers[2]);
    }

    [TestMethod]
    public async Task The_static_table_is_zero_based_and_includes_its_last_entry()
    {
        var headers = await ReadHeaders(Convert.FromHexString("0000c0ff23"), TestContext.CancellationToken);
        Assert.AreEqual(new KeyValuePair<string, string>(":authority", ""), headers[0]);
        Assert.AreEqual(new KeyValuePair<string, string>("x-frame-options", "sameorigin"), headers[1]);
    }

    [TestMethod]
    public async Task A_nonzero_delta_base_is_rejected_even_without_dynamic_references()
    {
        var block = new List<byte> { 0 };
        AppendInteger(block, 0, 7, (1UL << 62) - 1);
        block.Add(0xc1);
        await Assert.ThrowsExactlyAsync<Http3QpackException>(() => ReadHeaders(block.ToArray(), TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task A_zero_delta_base_ignores_its_sign_bit_like_the_reference()
    {
        var headers = await ReadHeaders([0, 0x80, 0xc1], TestContext.CancellationToken);
        Assert.AreEqual(new KeyValuePair<string, string>(":path", "/"), headers.Single());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("00")]
    [DataRow("ff")]
    [DataRow("0100")]
    [DataRow("0001d1")]
    [DataRow("00002000")]
    [DataRow("000080")]
    [DataRow("00004000")]
    [DataRow("000010")]
    [DataRow("00000000")]
    [DataRow("0000ff24")]
    [DataRow("0000500261")]
    [DataRow("00005f")]
    public async Task Invalid_prefixes_references_and_truncated_strings_fail_decompression(string hex)
    {
        var exception = await Assert.ThrowsExactlyAsync<Http3QpackException>(() =>
            ReadHeaders(Convert.FromHexString(hex), TestContext.CancellationToken));
        Assert.AreEqual(0x200L, exception.ErrorCode);
    }

    [TestMethod]
    public async Task Integer_overflow_and_unbounded_continuations_do_not_wrap()
    {
        byte[] overflow = [0, 0x7f, .. Enumerable.Repeat((byte)0xff, 8), 0x40];
        byte[] continuation = [0, 0x7f, .. Enumerable.Repeat((byte)0x80, 10), 0];
        await Assert.ThrowsExactlyAsync<Http3QpackException>(() => ReadHeaders(overflow, TestContext.CancellationToken));
        await Assert.ThrowsExactlyAsync<Http3QpackException>(() => ReadHeaders(continuation, TestContext.CancellationToken));
    }

    [TestMethod]
    [DataRow("f1e3c2e5f23a6ba0ab90f4ff", "www.example.com")]
    [DataRow("a8eb10649cbf", "no-cache")]
    [DataRow("25a849e95ba97d7f", "custom-key")]
    [DataRow("25a849e95bb8e8b4bf", "custom-value")]
    public void Published_huffman_vectors_decode(string hex, string expected)
    {
        Assert.AreEqual(expected, Http3QpackHuffman.Decode(Convert.FromHexString(hex), expected.Length));
    }

    [TestMethod]
    [DataRow("00")]
    [DataRow("ff")]
    [DataRow("ffff")]
    [DataRow("fffffffc")]
    [DataRow("ffffffff")]
    public void Invalid_huffman_padding_and_explicit_eos_fail(string hex)
    {
        Assert.ThrowsExactly<Http3QpackException>(() =>
            Http3QpackHuffman.Decode(Convert.FromHexString(hex), 100));
    }

    [TestMethod]
    public void Huffman_size_limit_applies_to_decoded_bytes()
    {
        // Five zero bits encode '0'; forty encoded zero bits expand from five bytes to eight chars.
        var encoded = new byte[5];
        Assert.AreEqual("00000000", Http3QpackHuffman.Decode(encoded, 8));
        Assert.ThrowsExactly<Http3HeaderLimitException>(() => Http3QpackHuffman.Decode(encoded, 7));
    }

    [TestMethod]
    public async Task Indexed_expansion_counts_name_and_value_bytes()
    {
        byte[] block = [0, 0, 0xc1, 0xc1];
        var exact = await ReadSection(block, new Http3ServerOptions { MaxHeaderBytes = 12 }, TestContext.CancellationToken);
        Assert.HasCount(2, exact.Fields);
        Assert.IsFalse(exact.ExceedsRequestLimits);
        var excessive = await ReadSection(block, new Http3ServerOptions { MaxHeaderBytes = 11 }, TestContext.CancellationToken);
        Assert.IsTrue(excessive.ExceedsRequestLimits);
        var tooMany = await ReadSection(block, new Http3ServerOptions { MaxHeaderCount = 1 }, TestContext.CancellationToken);
        Assert.IsTrue(tooMany.ExceedsRequestLimits);
    }

    [TestMethod]
    public async Task Default_decoded_size_limit_bounds_large_literals()
    {
        var options = new Http3ServerOptions();
        var value = new string('a', options.MaxHeaderBytes - 1);
        var exact = await ReadSection(CreateLiteralField("x", value), options, TestContext.CancellationToken);
        Assert.AreEqual(value, exact.Fields.Single().Value);
        Assert.IsFalse(exact.ExceedsRequestLimits);
        var excessive = await ReadSection(CreateLiteralField("x", value + "a"), options, TestContext.CancellationToken);
        Assert.IsTrue(excessive.ExceedsRequestLimits);
        await Assert.ThrowsExactlyAsync<Http3QpackException>(() =>
            ReadSection(CreateLiteralField("x", value + "aa"), options, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task Default_header_count_limit_bounds_many_short_fields()
    {
        var options = new Http3ServerOptions();
        byte[] maximum = [0, 0, .. Enumerable.Repeat((byte)0xc1, options.MaxHeaderCount)];
        byte[] excessive = [.. maximum, 0xc1];
        var exact = await ReadSection(maximum, options, TestContext.CancellationToken);
        Assert.HasCount(options.MaxHeaderCount, exact.Fields);
        Assert.IsFalse(exact.ExceedsRequestLimits);
        Assert.IsTrue((await ReadSection(excessive, options, TestContext.CancellationToken)).ExceedsRequestLimits);
    }

    [TestMethod]
    [DataRow(401, true)]
    [DataRow(400, false)]
    [DataRow(431, false)]
    [DataRow(503, false)]
    public async Task Empty_responses_have_the_expected_http_headers(int statusCode, bool challenge)
    {
        var date = new DateTimeOffset(2026, 10, 30, 10, 20, 30, TimeSpan.FromHours(2));
        var encoded = Http3Qpack.EncodeResponse(statusCode, CreateResponseHeaders(statusCode, date));
        Assert.AreEqual(0, encoded[0], "The response must not depend on a dynamic table.");
        Assert.AreEqual(0, encoded[1]);
        var headers = await ReadHeaders(encoded, TestContext.CancellationToken);
        Assert.AreEqual(":status", headers[0].Key);
        var fields = headers.ToDictionary(x => x.Key, x => x.Value);
        Assert.AreEqual(statusCode.ToString(CultureInfo.InvariantCulture), fields[":status"]);
        Assert.AreEqual("Fri, 30 Oct 2026 08:20:30 GMT", fields["date"]);
        Assert.AreEqual("Kestrel", fields["server"]);
        Assert.AreEqual("0", fields["content-length"]);
        Assert.AreEqual(challenge, fields.ContainsKey("www-authenticate"));
        if (challenge)
            Assert.AreEqual("Bearer", fields["www-authenticate"]);
    }

    [TestMethod]
    public void Challenge_field_representation_matches_the_measured_reference()
    {
        // A captured Kestrel field section with its Date replaced by this fixed UTC value. Equivalent
        // fields encoded with another static-name index or indexing flag are visible to a wire probe.
        byte[] expected = [
            0, 0, 0x5f, 0x30, 3, .. "401"u8,
            0x56, 29, .. "Fri, 30 Oct 2026 08:20:30 GMT"u8,
            0x5f, 0x4d, 7, .. "Kestrel"u8,
            0x37, 9, .. "www-authenticate"u8, 6, .. "Bearer"u8,
            0xc4
        ];
        var date = new DateTimeOffset(2026, 10, 30, 8, 20, 30, TimeSpan.Zero);
        CollectionAssert.AreEqual(expected, Http3Qpack.EncodeResponse(401, CreateResponseHeaders(401, date)));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(7)]
    [DataRow(4096)]
    public async Task Streaming_fields_survive_fragmentation_without_consuming_the_next_frame(int fragmentSize)
    {
        var block = Convert.FromHexString("0000d1d7c1508cf1e3c2e5f23a6ba0ab90f4ff");
        await using var stream = new FragmentedReadStream([.. block, 0x40, 0x7f], fragmentSize);
        await using var reader = new Http3StreamReader(stream);
        var section = await Http3Qpack.ReadSection(reader, block.Length, new Http3ServerOptions(), TestContext.CancellationToken);
        Assert.IsTrue(section.Completed);
        Assert.IsFalse(section.ExceedsRequestLimits);
        CollectionAssert.AreEqual((await ReadHeaders(block, TestContext.CancellationToken)).ToArray(), section.Fields.ToArray());
        Assert.AreEqual(0x40, await reader.ReadByte(TestContext.CancellationToken));
        Assert.AreEqual(0x7f, await reader.ReadByte(TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task Mixed_fields_and_compression_errors_have_the_same_result_at_every_fragment_boundary()
    {
        // Exercise literal names, name references, Huffman strings and multibyte indices together.
        // Every split must preserve both completed fields and the first error without reading the next frame.
        var valid = Convert.FromHexString("0000d1d7c1508cf1e3c2e5f23a6ba0ab90f4ff21780161ff23");
        var expected = await ReadHeaders(valid, TestContext.CancellationToken);
        byte[][] suffixes = [[], [0x5f], [0x50, 2, 0x61], [0xff, 0x24], [0x21, 0x78, 0x81, 0xff]];
        foreach (var suffix in suffixes) {
            byte[] block = [.. valid, .. suffix];
            for (var fragment = 1; fragment <= block.Length; fragment++) {
                await using var stream = new FragmentedReadStream([.. block, 0x40, 0x7f], fragment);
                await using var reader = new Http3StreamReader(stream);
                var observed = new List<KeyValuePair<string, string>>();
                var read = Http3Qpack.ReadSection(reader, block.Length, observed.Add,
                    new Http3ServerOptions(), TestContext.CancellationToken);
                if (suffix.Length == 0) {
                    Assert.IsTrue((await read).Completed);
                    Assert.AreEqual(0x40, await reader.ReadByte(TestContext.CancellationToken));
                }
                else {
                    await Assert.ThrowsExactlyAsync<Http3QpackException>(() => read);
                }
                CollectionAssert.AreEqual(expected.ToArray(), observed.ToArray(),
                    $"Different fields at fragment size {fragment}, suffix {Convert.ToHexString(suffix)}.");
            }
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task A_large_declared_string_keeps_pending_reads_bounded(int valueBytesReceived)
    {
        var bytes = new List<byte> { 0, 0, 0xd1, 0x21, (byte)'x' };
        AppendInteger(bytes, 0, 7, 32768);
        bytes.AddRange(Enumerable.Repeat((byte)'a', valueBytesReceived));
        await using var stream = new FragmentedReadStream(bytes.ToArray(), 1);
        await using var reader = new Http3StreamReader(stream);
        var section = await Http3Qpack.ReadSection(reader, 1024 * 1024, new Http3ServerOptions(),
            TestContext.CancellationToken);
        Assert.IsFalse(section.Completed);
        Assert.HasCount(1, section.Fields);
        Assert.AreEqual(new KeyValuePair<string, string>(":method", "GET"), section.Fields[0]);
        Assert.IsTrue(stream.LargestReadRequest <= 4096,
            "An advertised length must not create a correspondingly sized pending read buffer.");
    }

    [TestMethod]
    public async Task An_oversized_string_prefix_is_rejected_without_waiting_for_its_payload()
    {
        var bytes = new List<byte> { 0, 0, 0x21, (byte)'x' };
        AppendInteger(bytes, 0, 7, 32769);
        await using var stream = new FragmentedReadStream(bytes.ToArray(), 1);
        await using var reader = new Http3StreamReader(stream);
        await Assert.ThrowsExactlyAsync<Http3QpackException>(() =>
            Http3Qpack.ReadSection(reader, 1024 * 1024, new Http3ServerOptions(), TestContext.CancellationToken));
        Assert.IsTrue(stream.LargestReadRequest <= 4096);
    }

    [TestMethod]
    public async Task An_invalid_reference_is_rejected_before_the_rest_of_a_large_frame_arrives()
    {
        await using var stream = new FragmentedReadStream([0, 0, 0], 1);
        await using var reader = new Http3StreamReader(stream);
        await Assert.ThrowsExactlyAsync<Http3QpackException>(() =>
            Http3Qpack.ReadSection(reader, 1024 * 1024, new Http3ServerOptions(), TestContext.CancellationToken));
        Assert.AreEqual(3L, stream.Position);
        Assert.IsTrue(stream.LargestReadRequest <= 4096);
    }

    [TestMethod]
    public async Task Fin_before_any_complete_field_remains_incomplete()
    {
        await using var stream = new FragmentedReadStream([0], 1);
        await using var reader = new Http3StreamReader(stream);
        await Assert.ThrowsExactlyAsync<EndOfStreamException>(() =>
            Http3Qpack.ReadSection(reader, 5, new Http3ServerOptions(), TestContext.CancellationToken));
    }

    [TestMethod]
    [DataRow(new byte[] { 0, 0, 0xd1, 0xd1 })]
    [DataRow(new byte[] { 0, 0, 0xd1, 0x21, 0x41, 0 })]
    public async Task Malformed_complete_fields_are_rejected_even_before_the_section_finishes(byte[] prefix)
    {
        await using var stream = new FragmentedReadStream(prefix, 1);
        await using var reader = new Http3StreamReader(stream);
        var headers = new Http3RequestHeaders(trailers: false, new Http3ServerOptions().MaxRequestLineBytes);
        var exception = await Assert.ThrowsExactlyAsync<Http3ProtocolException>(() =>
            Http3Qpack.ReadSection(reader, 1024 * 1024, headers.Append, new Http3ServerOptions(),
                TestContext.CancellationToken));
        Assert.AreEqual(Http3Protocol.MessageError, exception.ErrorCode);
        Assert.IsFalse(exception.ConnectionError);
    }

    [TestMethod]
    [DataRow(100, false)]
    [DataRow(101, true)]
    [DataRow(200, true)]
    public async Task The_streaming_parser_preserves_the_references_header_count_allowance(int count,
        bool exceedsRequestLimits)
    {
        byte[] block = [0, 0, .. Enumerable.Repeat((byte)0xc1, count)];
        await using var stream = new FragmentedReadStream(block, 7);
        await using var reader = new Http3StreamReader(stream);
        var section = await Http3Qpack.ReadSection(reader, block.Length, new Http3ServerOptions(),
            TestContext.CancellationToken);
        Assert.IsTrue(section.Completed);
        Assert.AreEqual(exceedsRequestLimits, section.ExceedsRequestLimits);
        Assert.HasCount(count, section.Fields);
    }

    [TestMethod]
    public async Task The_201st_complete_field_exceeds_the_eager_parsing_allowance()
    {
        byte[] block = [0, 0, .. Enumerable.Repeat((byte)0xc1, 201)];
        await using var stream = new FragmentedReadStream(block, 7);
        await using var reader = new Http3StreamReader(stream);
        var exception = await Assert.ThrowsExactlyAsync<Http3ProtocolException>(() =>
            Http3Qpack.ReadSection(reader, block.Length, new Http3ServerOptions(), TestContext.CancellationToken));
        Assert.AreEqual(Http3Protocol.MessageError, exception.ErrorCode);
        Assert.IsFalse(exception.ConnectionError);
    }

    [TestMethod]
    [DataRow(32767, false)]
    [DataRow(32768, true)]
    public async Task Section_size_counts_name_and_value_separately_from_the_string_limit(int valueLength,
        bool exceedsRequestLimits)
    {
        var block = CreateLiteralField("x", new string('a', valueLength));
        await using var stream = new FragmentedReadStream(block, 19);
        await using var reader = new Http3StreamReader(stream);
        var section = await Http3Qpack.ReadSection(reader, block.Length, new Http3ServerOptions(),
            TestContext.CancellationToken);
        Assert.IsTrue(section.Completed);
        Assert.AreEqual(exceedsRequestLimits, section.ExceedsRequestLimits);
        Assert.AreEqual(valueLength, section.Fields.Single().Value.Length);
    }

    [TestMethod]
    public async Task Streaming_huffman_expansion_cannot_exceed_the_individual_string_limit()
    {
        const int decodedLength = 32769;
        var encoded = new byte[(decodedLength * 5 + 7) / 8];
        encoded[^1] = (byte)((1 << (encoded.Length * 8 - decodedLength * 5)) - 1);
        var block = new List<byte> { 0, 0, 0x21, (byte)'x' };
        AppendInteger(block, 0x80, 7, (ulong)encoded.Length);
        block.AddRange(encoded);
        await using var stream = new FragmentedReadStream(block.ToArray(), 31);
        await using var reader = new Http3StreamReader(stream);
        await Assert.ThrowsExactlyAsync<Http3QpackException>(() =>
            Http3Qpack.ReadSection(reader, block.Count, new Http3ServerOptions(), TestContext.CancellationToken));
    }

    private static async Task<IReadOnlyList<KeyValuePair<string, string>>> ReadHeaders(byte[] block,
        CancellationToken cancellationToken) =>
        (await ReadSection(block, new Http3ServerOptions(), cancellationToken)).Fields;

    private static async Task<Http3FieldSection> ReadSection(byte[] block, Http3ServerOptions options,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(block);
        await using var reader = new Http3StreamReader(stream);
        return await Http3Qpack.ReadSection(reader, block.Length, options, cancellationToken);
    }

    private static IReadOnlyList<KeyValuePair<string, string>> CreateResponseHeaders(int statusCode,
        DateTimeOffset date)
    {
        List<KeyValuePair<string, string>> headers = [
            new("date", date.UtcDateTime.ToString("R", CultureInfo.InvariantCulture)),
            new("server", "Kestrel")
        ];
        if (statusCode == 401)
            headers.Add(new KeyValuePair<string, string>("www-authenticate", "Bearer"));
        headers.Add(new KeyValuePair<string, string>("content-length", "0"));
        return headers;
    }

    private sealed class FragmentedReadStream(byte[] bytes, int maximumReadSize) : MemoryStream(bytes)
    {
        public int LargestReadRequest { get; private set; }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            LargestReadRequest = Math.Max(LargestReadRequest, buffer.Length);
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, maximumReadSize)], cancellationToken);
        }
    }

    private static byte[] CreateLiteralField(string name, string value)
    {
        var block = new List<byte> { 0, 0 };
        AppendInteger(block, 0x20, 3, (ulong)name.Length);
        block.AddRange(Encoding.ASCII.GetBytes(name));
        AppendInteger(block, 0, 7, (ulong)value.Length);
        block.AddRange(Encoding.ASCII.GetBytes(value));
        return block.ToArray();
    }

    private static void AppendInteger(List<byte> bytes, byte prefix, int prefixBits, ulong value)
    {
        var mask = (1UL << prefixBits) - 1;
        bytes.Add((byte)(prefix | Math.Min(value, mask)));
        if (value < mask)
            return;
        value -= mask;
        while (value >= 128) {
            bytes.Add((byte)(0x80 | (value & 0x7f)));
            value >>= 7;
        }
        bytes.Add((byte)value);
    }
}
