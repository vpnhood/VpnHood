using System;
using System.IO;

namespace VpnHood.Tunneling
{
    public class StreamHeadCryptor : Stream
    {
        private readonly Stream _stream;
        private readonly long _maxCipherCount;
        private readonly BufferCryptor _bufferCryptor;
        public readonly bool _leaveOpen;

        private long _readCount;
        private long _writeCount;

        public static StreamHeadCryptor Create(Stream stream, byte[] key, byte[] sault, long maxCipherPos, bool leaveOpen = false)
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

            return new StreamHeadCryptor(stream, encKey, maxCipherPos, leaveOpen);
        }

        public StreamHeadCryptor(Stream stream, byte[] key, long maxCipherPos, bool leaveOpen = false)
        {
            _stream = stream;
            _bufferCryptor = new BufferCryptor(key);
            _maxCipherCount = maxCipherPos;
            _leaveOpen = leaveOpen;
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
                _bufferCryptor.Cipher(buffer, offset, (int)cipherCount, _readCount);
                _readCount += ret;
            }
            return ret;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            var cipherCount = Math.Min(count, _maxCipherCount - _writeCount);
            if (cipherCount > 0)
            {
                _bufferCryptor.Cipher(buffer, offset, (int)cipherCount, _writeCount);
                _writeCount += cipherCount;
            }

            _stream.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _bufferCryptor.Dispose();
                if (!_leaveOpen)
                    _stream.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
