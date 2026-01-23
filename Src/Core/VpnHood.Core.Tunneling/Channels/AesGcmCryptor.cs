using System.Security.Cryptography;

namespace VpnHood.Core.Tunneling.Channels;

internal sealed class AesGcmCryptor(ReadOnlySpan<byte> key, int tagLength) : IChannelCryptor
{
    private readonly AesGcm _aesGcm = new(key, tagLength);

    public void Encrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plainText, Span<byte> cipherText, Span<byte> tag,
        ReadOnlySpan<byte> associatedData)
    {
        _aesGcm.Encrypt(nonce, plainText, cipherText, tag, associatedData);
    }

    public void Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> cipherText, ReadOnlySpan<byte> tag, Span<byte> plainText,
        ReadOnlySpan<byte> associatedData)
    {
        _aesGcm.Decrypt(nonce, cipherText, tag, plainText, associatedData);
    }

    public void Dispose()
    {
        _aesGcm.Dispose();
    }
}
