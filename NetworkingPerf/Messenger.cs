using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkingPerf
{
    sealed class Messenger
    {
        private readonly Stream inner;
        private byte[] headerBuffer;
        
        public Messenger(Stream inner)
        {
            this.inner = inner;
            this.headerBuffer = new byte[4];
        }

        public async ValueTask<Buffer> ReadAsync()
        {
            // read out 4 bytes representing length
            await inner.ReadAsync(headerBuffer, 0, 4);
            var length = (headerBuffer[0] << 24) +
                (headerBuffer[1] << 16) +
                (headerBuffer[2] << 8) +
                headerBuffer[3];
            if (length == 0)
            {
                return new Buffer(0);
            }

            var buffer = new Buffer(length);//new byte[lenth];
            var data = buffer.Data;

            int read = 0;
            while (read < length)
            {
                read += await inner.ReadAsync(data.Array, data.Offset + read, length - (read + data.Offset));
            }
            return buffer;
        }

        public async Task WriteAsync(Buffer message)
        {
            byte[] payload = null;
            try
            {
                var data = message.Data;
                var length = data.Count;
                payload = BufferPool.Instance.Rent(length + 4);
                payload[0] = (byte)(length >> 24);
                payload[1] = (byte)(length >> 16);
                payload[2] = (byte)(length >> 8);
                payload[3] = (byte)length;
                Array.Copy(data.Array, data.Offset, payload, 4, length);
                message.Dispose();
                await inner.WriteAsync(payload, 0, length + 4);
            }
            finally
            {
                if (payload != null)
                {
                    BufferPool.Instance.Return(payload);
                }
            }
        }
    }

    public sealed class Buffer : IDisposable
    {
        readonly byte[] inner;
        int length;
        int disposed;

        public Buffer(int length)
        {
            this.length = length;
            if (length > 0)
                this.inner = BufferPool.Instance.Rent(length);
            else
                this.inner = Array.Empty<byte>();
        }

        public ArraySegment<byte> Data => new ArraySegment<byte>(this.inner, 0, length);

        public void Dispose()
        {
            if (this.length > 0)
            {
                if (Interlocked.Exchange(ref this.disposed, 1) == 0)
                {
                    BufferPool.Instance.Return(this.inner);
                }
            }
        }
    }

    static class BufferPool
    {
        public static readonly ArrayPool<byte> Instance = ArrayPool<byte>.Shared;
    }
}
