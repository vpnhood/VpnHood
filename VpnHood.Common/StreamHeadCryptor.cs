using System;
using System.IO;
using System.Security.Cryptography;

namespace VpnHood
{
    public class StreamHeadCryptor : Stream
    {
        private readonly Stream _stream;
        private readonly ICryptoTransform _crypto;
        private readonly long _maxCipherCount;
        public readonly bool _leaveOpen;

        private long _readCount;
        private long _writeCount;

        public static StreamHeadCryptor CreateAesCryptor(Stream stream, byte[] key, byte[] sault, long maxCipherPos, bool leaveOpen = false)
        {
            var encKey = key;

            // apply sault if sault exists
            if (sault != null)
            {
                if (key.Length != sault.Length) throw new Exception($"{nameof(key)} length and {nameof(sault)} length is not same!");
                encKey = (byte[])key.Clone();
                for (var i = 0; i < encKey.Length; i++)
                    encKey[i] ^= sault[i];
            }

            using var aes = Aes.Create();
            aes.KeySize = encKey.Length * 8;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            aes.Key = encKey;
            var crytpo = aes.CreateEncryptor(aes.Key, aes.IV);
            return new StreamHeadCryptor(stream, crytpo, maxCipherPos, leaveOpen);
        }

        public StreamHeadCryptor(Stream stream, ICryptoTransform crypto, long maxCipherPos, bool leaveOpen = false)
        {
            _stream = stream;
            _crypto = crypto;
            _maxCipherCount = maxCipherPos;
            _leaveOpen = leaveOpen;
        }

        private void Cipher(byte[] buffer, int offset, int count, long streamPos)
        {
            //find block number
            var blockSizeInByte = _crypto.OutputBlockSize;
            var blockNumber = (streamPos / blockSizeInByte) + 1;
            var keyPos = streamPos % blockSizeInByte;

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

        public override bool CanRead => _stream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => _stream.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() => _stream.Flush();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var ret = _stream.Read(buffer, offset, count);

            var cipherCount = Math.Min(count, _maxCipherCount - _readCount);
            if (cipherCount > 0)
            {
                Cipher(buffer, offset, (int)cipherCount, _readCount);
                _readCount += ret;
            }
            return ret;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            var cipherCount = Math.Min(count, _maxCipherCount - _writeCount);
            if (cipherCount > 0)
            {
                Cipher(buffer, offset, (int)cipherCount, _writeCount);
                _writeCount += cipherCount;
            }

            _stream.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _crypto.Dispose();
                if (!_leaveOpen)
                    _stream.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
