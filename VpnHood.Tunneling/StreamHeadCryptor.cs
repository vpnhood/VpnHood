using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VpnHood.Tunneling
{
    public class StreamHeadCryptor : Stream
    {
        private readonly BufferCryptor _bufferCryptor;
        private readonly bool _leaveOpen;
        private readonly long _maxCipherCount;
        private readonly Stream _stream;

        private long _readCount;
        private long _writeCount;

        public StreamHeadCryptor(Stream stream, byte[] key, long maxCipherPos, bool leaveOpen = false)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));

            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
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

        public static StreamHeadCryptor Create(Stream stream, byte[] key, byte[]? salt, long maxCipherPos,
            bool leaveOpen = false)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            if (key is null) throw new ArgumentNullException(nameof(key));

            var encKey = key;

            // apply salt if salt exists
            if (salt != null)
            {
                if (key.Length != salt.Length)
                    throw new Exception($"{nameof(key)} length and {nameof(salt)} length is not same!");
                encKey = (byte[]) key.Clone();
                for (var i = 0; i < encKey.Length; i++)
                    encKey[i] ^= salt[i];
            }

            return new StreamHeadCryptor(stream, encKey, maxCipherPos, leaveOpen);
        }

        public override void Flush()
        {
            _stream.Flush();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        private void PrepareReadBuffer(byte[] buffer, int offset, int count, int readCount)
        {
            var cipherCount = Math.Min(count, _maxCipherCount - _readCount);
            if (cipherCount > 0)
            {
                _bufferCryptor.Cipher(buffer, offset, (int) cipherCount, _readCount);
                _readCount += readCount;
            }
        }

        private void PrepareWriteBuffer(byte[] buffer, int offset, int count)
        {
            var cipherCount = Math.Min(count, _maxCipherCount - _writeCount);
            if (cipherCount > 0)
            {
                _bufferCryptor.Cipher(buffer, offset, (int) cipherCount, _writeCount);
                _writeCount += cipherCount;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var ret = _stream.Read(buffer, offset, count);
            PrepareReadBuffer(buffer, offset, count, ret);
            return ret;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken)
        {
            var ret = await _stream.ReadAsync(buffer, offset, count, cancellationToken);
            PrepareReadBuffer(buffer, offset, count, ret);
            return ret;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            PrepareWriteBuffer(buffer, offset, count);
            _stream.Write(buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            PrepareWriteBuffer(buffer, offset, count);
            return _stream.WriteAsync(buffer, offset, count, cancellationToken);
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