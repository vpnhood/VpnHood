using System.Security.Cryptography;

namespace VpnHood.Tunneling;

public class BufferCryptor : IDisposable
{
    private readonly ICryptoTransform _cryptor;

    public BufferCryptor(byte[] key)
    {
        //init cryptor
        using var aes = Aes.Create();
        aes.KeySize = key.Length * 8;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        _cryptor = aes.CreateEncryptor(aes.Key, aes.IV);
    }

    public void Dispose()
    {
        _cryptor.Dispose();
    }

    public void Cipher(byte[] buffer, int offset, int count, long cryptoPos)
    {
        //find block number
        var blockSizeInByte = (uint)_cryptor.OutputBlockSize;
        var blockNumber = (ulong)cryptoPos / blockSizeInByte + 1;
        var keyPos = (ulong)cryptoPos % blockSizeInByte;

        //buffer
        var outputBuffer = new byte[blockSizeInByte];
        var nonce = new byte[blockSizeInByte];
        var init = false;

        for (var i = offset; i < offset + count; i++) {
            //encrypt the nonce to form next xor buffer (unique key)
            if (!init || keyPos % blockSizeInByte == 0) {
                BitConverter.GetBytes(blockNumber).CopyTo(nonce, 0);
                _cryptor.TransformBlock(nonce, 0, nonce.Length, outputBuffer, 0);
                if (init) keyPos = 0;
                init = true;
                blockNumber++;
            }

            buffer[i] ^= outputBuffer[keyPos]; //simple XOR with generated unique key
            keyPos++;
        }
    }
}