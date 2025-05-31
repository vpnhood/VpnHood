using System.Buffers.Binary;
using System.Security.Cryptography;

namespace VpnHood.Core.Tunneling;

public class BufferCryptor : IDisposable
{
    private readonly ICryptoTransform _cryptor;
    private readonly byte[] _cipherBuffer;
    private readonly byte[] _cipherNonce;

    public BufferCryptor(byte[] key)
    {
        //init cryptor
        using var aes = Aes.Create();
        aes.KeySize = key.Length * 8;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        _cryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        _cipherBuffer = new byte[_cryptor.OutputBlockSize];
        _cipherNonce = new byte[_cryptor.OutputBlockSize];
    }

    public void Dispose()
    {
        _cryptor.Dispose();
    }

    // not thread-safe
    public void Cipher(Span<byte> buffer, long cryptoPos)
    {
        //find block number
        var blockSizeInByte = (uint)_cryptor.OutputBlockSize;
        var blockNumber = (ulong)cryptoPos / blockSizeInByte + 1;
        var keyPos = (ulong)cryptoPos % blockSizeInByte;

        //buffer
        var init = false;
        for (var i = 0; i < buffer.Length; i++) {
            //encrypt the nonce to form next xor buffer (unique key)
            if (!init || keyPos % blockSizeInByte == 0) {
                BinaryPrimitives.WriteUInt64LittleEndian(_cipherNonce.AsSpan(0, 8), blockNumber);
                _cryptor.TransformBlock(_cipherNonce, 0, _cipherNonce.Length, _cipherBuffer, 0);
                if (init) keyPos = 0;
                init = true;
                blockNumber++;
            }

            buffer[i] ^= _cipherBuffer[keyPos]; //simple XOR with generated unique key
            keyPos++;
        }
    }
}