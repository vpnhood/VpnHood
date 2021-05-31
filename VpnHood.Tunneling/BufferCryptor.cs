using System;
using System.Security.Cryptography;

namespace VpnHood.Tunneling
{
    public class BufferCryptor : IDisposable
    {
        private readonly ICryptoTransform _crypto;

        public BufferCryptor(byte[] key)
        {
            //init cryptor
            using var aes = Aes.Create();
            aes.KeySize = key.Length * 8;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            aes.Key = key;
            _crypto = aes.CreateEncryptor(aes.Key, aes.IV);
        }

        public void Cipher(byte[] buffer, int offset, int count, long cryptoPos)
        {
            //find block number
            var blockSizeInByte = _crypto.OutputBlockSize;
            var blockNumber = (cryptoPos / blockSizeInByte) + 1;
            var keyPos = cryptoPos % blockSizeInByte;

            //buffer
            var outputBuffer = new byte[blockSizeInByte];
            var nonce = new byte[blockSizeInByte];
            var init = false;

            for (int i = offset; i < count; i++)
            {
                //encrypt the nonce to form next xor buffer (unique key)
                if (!init || (keyPos % blockSizeInByte) == 0)
                {
                    BitConverter.GetBytes(blockNumber).CopyTo(nonce, 0);
                    _crypto.TransformBlock(nonce, 0, nonce.Length, outputBuffer, 0);
                    if (init) keyPos = 0;
                    init = true;
                    blockNumber++;
                }
                buffer[i] ^= outputBuffer[keyPos]; //simple XOR with generated unique key
                keyPos++;
            }
        }

        public void Dispose()
        {
            _crypto.Dispose();
        }
    }
}
