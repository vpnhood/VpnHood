using System.Buffers.Binary;
using System.Security.Cryptography;

namespace VpnHood.Core.Tunneling.Channels;

internal sealed class AesCtrCryptor : IChannelCryptor
{
    private readonly Aes _aes;
    private readonly ICryptoTransform _encryptor;

    public AesCtrCryptor(ReadOnlySpan<byte> key)
    {
        _aes = Aes.Create();
        _aes.Mode = CipherMode.ECB;
        _aes.Padding = PaddingMode.None;
        _aes.Key = key.ToArray();
        _encryptor = _aes.CreateEncryptor();
    }

    public void Encrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plainText, Span<byte> cipherText, Span<byte> tag,
        ReadOnlySpan<byte> associatedData)
    {
        _ = associatedData;
        Transform(nonce, plainText, cipherText);
    }

    public void Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> cipherText, ReadOnlySpan<byte> tag,
        Span<byte> plainText, ReadOnlySpan<byte> associatedData)
    {

        _ = associatedData;
        Transform(nonce, cipherText, plainText);
    }

    private void Transform(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (nonce.Length != 12)
            throw new ArgumentOutOfRangeException(nameof(nonce), "Nonce must be 12 bytes.");

        var counterBlock = new byte[16];
        var keystreamBlock = new byte[16];
        nonce.CopyTo(counterBlock);

        var offset = 0;
        uint counter = 0;
        while (offset < input.Length) {
            BinaryPrimitives.WriteUInt32LittleEndian(counterBlock.AsSpan(12, 4), counter++);
            _encryptor.TransformBlock(counterBlock, 0, 16, keystreamBlock, 0);

            var remaining = input.Length - offset;
            var chunk = remaining > 16 ? 16 : remaining;
            for (var i = 0; i < chunk; i++)
                output[offset + i] = (byte)(input[offset + i] ^ keystreamBlock[i]);

            offset += chunk;
        }
    }

    public void Dispose()
    {
        _encryptor.Dispose();
        _aes.Dispose();
    }
}
