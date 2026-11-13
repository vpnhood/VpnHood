using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using VpnHood.Net.Toolkit.Streams;

namespace VpnHood.Test.Tests;

[TestClass]
public class StreamUtilsTest : TestBase
{
    // ReSharper disable once ClassNeverInstantiated.Local
    private sealed record Item(string Name);

    // a message as it travels: its length in four bytes, then its bytes
    private static MemoryStream CreateFrame(int length, byte[] body)
    {
        var frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, length);
        body.CopyTo(frame, 4);
        return new MemoryStream(frame);
    }

    [TestMethod]
    public async Task An_object_is_read_as_it_was_written()
    {
        using var stream = new MemoryStream();
        await StreamUtils.WriteObjectAsync(stream, new Item("bücher"), TestCt);
        stream.Position = 0;
        Assert.AreEqual("bücher", (await StreamUtils.ReadObjectAsync<Item>(stream, TestCt)).Name);

        stream.Position = 0;
        StringAssert.Contains(await StreamUtils.ReadMessageAsync(stream, TestCt), "Name");
    }

    [TestMethod]
    public async Task A_negative_length_is_refused_as_a_length()
    {
        await Assert.ThrowsExactlyAsync<FormatException>(() =>
            StreamUtils.ReadObjectAsync<Item>(CreateFrame(-1, []), TestCt));
        await Assert.ThrowsExactlyAsync<FormatException>(() =>
            StreamUtils.ReadMessageAsync(CreateFrame(int.MinValue, []), TestCt));
        Assert.ThrowsExactly<FormatException>(() => StreamUtils.ReadObject<Item>(CreateFrame(-1, [])));
    }

    // 0xFF is no byte of UTF-8: read leniently, it became a replacement character inside the name, and passed
    [TestMethod]
    public async Task Bytes_that_are_no_utf8_are_refused()
    {
        byte[] body = [.. "{\"Name\":\"a"u8, 0xFF, .. "b\"}"u8];

        await Assert.ThrowsAsync<JsonException>(() =>
            StreamUtils.ReadObjectAsync<Item>(CreateFrame(body.Length, body), TestCt));
        await Assert.ThrowsExactlyAsync<DecoderFallbackException>(() =>
            StreamUtils.ReadMessageAsync(CreateFrame(body.Length, body), TestCt));
    }
}
