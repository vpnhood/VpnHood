using System.Buffers.Binary;
using System.Security.Cryptography;

namespace VpnHood.Core.Tunneling.Channels;

// We tried AesGcm & ChaCha20Poly1305, but they were significantly slower than AesCtr for our use case.
internal sealed class AesCtrCryptor : IDisposable
{
    private readonly Aes _aes;
    private readonly ICryptoTransform _encryptor;
    private readonly byte[] _counterBlock = new byte[16];
    private readonly byte[] _keystreamBlock = new byte[16];
    private readonly Lock _lock = new();

    public AesCtrCryptor(ReadOnlySpan<byte> key)
    {
        _aes = Aes.Create();
        _aes.Mode = CipherMode.ECB;
        _aes.Padding = PaddingMode.None;
        _aes.Key = key.ToArray();
        _encryptor = _aes.CreateEncryptor();
    }

    public void Encrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plainText, Span<byte> cipherText)
    {
        Transform(nonce, plainText, cipherText);
    }

    public void Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> cipherText, Span<byte> plainText)
    {
        Transform(nonce, cipherText, plainText);
    }

    private void Transform(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (nonce.Length != 12)
            throw new ArgumentOutOfRangeException(nameof(nonce), "Nonce must be 12 bytes.");

        lock (_lock) {
            nonce.CopyTo(_counterBlock);

            var offset = 0;
            uint counter = 0;
            while (offset < input.Length) {
                BinaryPrimitives.WriteUInt32LittleEndian(_counterBlock.AsSpan(12, 4), counter++);
                _encryptor.TransformBlock(_counterBlock, 0, 16, _keystreamBlock, 0);

                var remaining = input.Length - offset;
                var chunk = remaining > 16 ? 16 : remaining;
                for (var i = 0; i < chunk; i++)
                    output[offset + i] = (byte)(input[offset + i] ^ _keystreamBlock[i]);

                offset += chunk;
            }
        }
    }

    public void Dispose()
    {
        _encryptor.Dispose();
        _aes.Dispose();
    }
}
