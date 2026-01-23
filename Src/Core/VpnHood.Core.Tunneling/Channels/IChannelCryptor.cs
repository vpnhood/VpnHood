namespace VpnHood.Core.Tunneling.Channels;

internal interface IChannelCryptor : IDisposable
{
    void Encrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plainText, Span<byte> cipherText, Span<byte> tag,
        ReadOnlySpan<byte> associatedData);

    void Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> cipherText, ReadOnlySpan<byte> tag, Span<byte> plainText,
        ReadOnlySpan<byte> associatedData);
}
