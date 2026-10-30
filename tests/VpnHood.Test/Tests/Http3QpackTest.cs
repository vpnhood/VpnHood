using System.Globalization;
using System.Text;
using VpnHood.Core.Server.Listeners.Http3;

namespace VpnHood.Test.Tests;

[TestClass]
public class Http3QpackTest
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    public void Rfc9204_literal_name_reference_example_decodes()
    {
        // RFC 9204 Appendix B.1: :path = /index.html, without a dynamic table.
        var headers = Http3Qpack.Decode(Convert.FromHexString("0000510b2f696e6465782e68746d6c"));
        CollectionAssert.AreEqual(new[] { new KeyValuePair<string, string>(":path", "/index.html") },
            headers.ToArray());
    }

    [TestMethod]
    public void A_browser_request_can_mix_static_fields_and_huffman_authority()
    {
        // The authority's Huffman bytes are RFC 7541 Appendix C.4.1's www.example.com vector.
        var headers = Http3Qpack.Decode(Convert.FromHexString("0000d1d7c1508cf1e3c2e5f23a6ba0ab90f4ff"));
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
    public void Huffman_literal_names_and_never_indexed_flags_decode(string first)
    {
        // RFC 7541 C.4.3's custom-key and custom-value strings, in QPACK's literal-name form.
        var headers = Http3Qpack.Decode(Convert.FromHexString(
            "0000" + first + "0125a849e95ba97d7f8925a849e95bb8e8b4bf"));
        Assert.HasCount(1, headers);
        Assert.AreEqual("custom-key", headers[0].Key);
        Assert.AreEqual("custom-value", headers[0].Value);
    }

    [TestMethod]
    public void Header_order_duplicates_and_opaque_octets_are_preserved()
    {
        var headers = Http3Qpack.Decode(Convert.FromHexString("0000217801613178016221780180"));
        Assert.HasCount(3, headers);
        Assert.AreEqual(new KeyValuePair<string, string>("x", "a"), headers[0]);
        Assert.AreEqual(new KeyValuePair<string, string>("x", "b"), headers[1]);
        Assert.AreEqual(new KeyValuePair<string, string>("x", "\u0080"), headers[2]);
    }

    [TestMethod]
    public void The_static_table_is_zero_based_and_includes_its_last_entry()
    {
        var headers = Http3Qpack.Decode(Convert.FromHexString("0000c0ff23"));
        Assert.AreEqual(new KeyValuePair<string, string>(":authority", ""), headers[0]);
        Assert.AreEqual(new KeyValuePair<string, string>("x-frame-options", "sameorigin"), headers[1]);
    }

    [TestMethod]
    public void A_positive_base_is_legal_without_dynamic_references_even_at_62_bits()
    {
        var block = new List<byte> { 0 };
        AppendInteger(block, 0, 7, (1UL << 62) - 1);
        block.Add(0xc1);
        var headers = Http3Qpack.Decode(block.ToArray());
        Assert.AreEqual(new KeyValuePair<string, string>(":path", "/"), headers.Single());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("00")]
    [DataRow("ff")]
    [DataRow("0100")]
    [DataRow("0080d1")]
    [DataRow("000080")]
    [DataRow("00004000")]
    [DataRow("000010")]
    [DataRow("00000000")]
    [DataRow("0000ff24")]
    [DataRow("0000500261")]
    [DataRow("00005f")]
    public void Invalid_prefixes_references_and_truncated_strings_fail_decompression(string hex)
    {
        var exception = Assert.ThrowsExactly<Http3QpackException>(() =>
            Http3Qpack.Decode(Convert.FromHexString(hex)));
        Assert.AreEqual(0x200L, exception.ErrorCode);
    }

    [TestMethod]
    public void Integer_overflow_and_unbounded_continuations_do_not_wrap()
    {
        byte[] overflow = [0, 0x7f, .. Enumerable.Repeat((byte)0xff, 8), 0x40];
        byte[] continuation = [0, 0x7f, .. Enumerable.Repeat((byte)0x80, 10), 0];
        Assert.ThrowsExactly<Http3QpackException>(() => Http3Qpack.Decode(overflow));
        Assert.ThrowsExactly<Http3QpackException>(() => Http3Qpack.Decode(continuation));
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
    public void Indexed_expansion_counts_decoded_size_and_field_overhead()
    {
        byte[] block = [0, 0, 0xc1, 0xc1]; // Two :path=/ fields, each five + one + thirty-two bytes.
        Assert.HasCount(2, Http3Qpack.Decode(block, maxHeaderBytes: 76));
        Assert.ThrowsExactly<Http3HeaderLimitException>(() => Http3Qpack.Decode(block, maxHeaderBytes: 75));
        Assert.ThrowsExactly<Http3HeaderLimitException>(() => Http3Qpack.Decode(block, maxHeaderCount: 1));
    }

    [TestMethod]
    public void Default_decoded_size_limit_bounds_large_literals()
    {
        var value = new string('a', Http3Qpack.MaxHeaderBytes - 33); // x + value + per-field overhead.
        var block = CreateLiteralField("x", value);
        Assert.AreEqual(value, Http3Qpack.Decode(block).Single().Value);
        Assert.ThrowsExactly<Http3HeaderLimitException>(() =>
            Http3Qpack.Decode(CreateLiteralField("x", value + "a")));
    }

    [TestMethod]
    public void Default_header_count_limit_bounds_many_short_fields()
    {
        byte[] maximum = [0, 0, .. Enumerable.Repeat((byte)0xc1, Http3Qpack.MaxHeaderCount)];
        byte[] excessive = [.. maximum, 0xc1];
        Assert.HasCount(Http3Qpack.MaxHeaderCount, Http3Qpack.Decode(maximum));
        Assert.ThrowsExactly<Http3HeaderLimitException>(() => Http3Qpack.Decode(excessive));
    }

    [TestMethod]
    [DataRow(401, true)]
    [DataRow(400, false)]
    [DataRow(431, false)]
    [DataRow(503, false)]
    public void Empty_responses_have_the_expected_http_headers(int statusCode, bool challenge)
    {
        var date = new DateTimeOffset(2026, 10, 30, 10, 20, 30, TimeSpan.FromHours(2));
        var encoded = Http3Qpack.EncodeResponse(statusCode, date);
        Assert.AreEqual(0, encoded[0], "The response must not depend on a dynamic table.");
        Assert.AreEqual(0, encoded[1]);
        var headers = Http3Qpack.Decode(encoded);
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
        CollectionAssert.AreEqual(expected, Http3Qpack.EncodeResponse(401, date));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(7)]
    [DataRow(4096)]
    public async Task Streaming_fields_survive_fragmentation_without_consuming_the_next_frame(int fragmentSize)
    {
        var block = Convert.FromHexString("0000d1d7c1508cf1e3c2e5f23a6ba0ab90f4ff");
        using var stream = new FragmentedReadStream([.. block, 0x40, 0x7f], fragmentSize);
        await using var reader = new Http3StreamReader(stream);
        var section = await Http3Qpack.ReadSection(reader, block.Length, TestContext.CancellationToken);
        Assert.IsTrue(section.Completed);
        Assert.IsFalse(section.ExceedsRequestLimits);
        CollectionAssert.AreEqual(Http3Qpack.Decode(block).ToArray(), section.Fields.ToArray());
        Assert.AreEqual(0x40, await reader.ReadByte(TestContext.CancellationToken));
        Assert.AreEqual(0x7f, await reader.ReadByte(TestContext.CancellationToken));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task A_large_declared_string_keeps_pending_reads_bounded(int valueBytesReceived)
    {
        var bytes = new List<byte> { 0, 0, 0xd1, 0x21, (byte)'x' };
        AppendInteger(bytes, 0, 7, 32768);
        bytes.AddRange(Enumerable.Repeat((byte)'a', valueBytesReceived));
        using var stream = new FragmentedReadStream(bytes.ToArray(), 1);
        await using var reader = new Http3StreamReader(stream);
        var section = await Http3Qpack.ReadSection(reader, 1024 * 1024,
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
        using var stream = new FragmentedReadStream(bytes.ToArray(), 1);
        await using var reader = new Http3StreamReader(stream);
        await Assert.ThrowsExactlyAsync<Http3QpackException>(() =>
            Http3Qpack.ReadSection(reader, 1024 * 1024, TestContext.CancellationToken));
        Assert.IsTrue(stream.LargestReadRequest <= 4096);
    }

    [TestMethod]
    public async Task An_invalid_reference_is_rejected_before_the_rest_of_a_large_frame_arrives()
    {
        using var stream = new FragmentedReadStream([0, 0, 0], 1);
        await using var reader = new Http3StreamReader(stream);
        await Assert.ThrowsExactlyAsync<Http3QpackException>(() =>
            Http3Qpack.ReadSection(reader, 1024 * 1024, TestContext.CancellationToken));
        Assert.AreEqual(3L, stream.Position);
        Assert.IsTrue(stream.LargestReadRequest <= 4096);
    }

    [TestMethod]
    public async Task Fin_before_any_complete_field_remains_incomplete()
    {
        using var stream = new FragmentedReadStream([0], 1);
        await using var reader = new Http3StreamReader(stream);
        await Assert.ThrowsExactlyAsync<EndOfStreamException>(() =>
            Http3Qpack.ReadSection(reader, 5, TestContext.CancellationToken));
    }

    [TestMethod]
    [DataRow(new byte[] { 0, 0, 0xd1, 0xd1 })]
    [DataRow(new byte[] { 0, 0, 0xd1, 0x21, 0x41, 0 })]
    public async Task Malformed_complete_fields_are_rejected_even_before_the_section_finishes(byte[] prefix)
    {
        using var stream = new FragmentedReadStream(prefix, 1);
        await using var reader = new Http3StreamReader(stream);
        var headers = new Http3RequestHeaders(trailers: false);
        var exception = await Assert.ThrowsExactlyAsync<Http3ProtocolException>(() =>
            Http3Qpack.ReadSection(reader, 1024 * 1024, headers.Append,
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
        using var stream = new FragmentedReadStream(block, 7);
        await using var reader = new Http3StreamReader(stream);
        var section = await Http3Qpack.ReadSection(reader, block.Length,
            TestContext.CancellationToken);
        Assert.IsTrue(section.Completed);
        Assert.AreEqual(exceedsRequestLimits, section.ExceedsRequestLimits);
        Assert.HasCount(count, section.Fields);
    }

    [TestMethod]
    public async Task The_201st_complete_field_exceeds_the_eager_parsing_allowance()
    {
        byte[] block = [0, 0, .. Enumerable.Repeat((byte)0xc1, 201)];
        using var stream = new FragmentedReadStream(block, 7);
        await using var reader = new Http3StreamReader(stream);
        var exception = await Assert.ThrowsExactlyAsync<Http3ProtocolException>(() =>
            Http3Qpack.ReadSection(reader, block.Length, TestContext.CancellationToken));
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
        using var stream = new FragmentedReadStream(block, 19);
        await using var reader = new Http3StreamReader(stream);
        var section = await Http3Qpack.ReadSection(reader, block.Length,
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
        using var stream = new FragmentedReadStream(block.ToArray(), 31);
        await using var reader = new Http3StreamReader(stream);
        await Assert.ThrowsExactlyAsync<Http3QpackException>(() =>
            Http3Qpack.ReadSection(reader, block.Count, TestContext.CancellationToken));
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
