using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Text;
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

        public async ValueTask<(int Length, IMemoryOwner<byte> Memory)> ReadAsync()
        {
            // read out 4 bytes representing length
            await ReadExactAsync(headerBuffer);
            var length = (headerBuffer[0] << 24) +
                (headerBuffer[1] << 16) +
                (headerBuffer[2] << 8) +
                headerBuffer[3];

            var buffer = MemoryPool<byte>.Shared.Rent(length);
            var data = buffer.Memory;
            await ReadExactAsync(data.Slice(0, length));
            return (length, buffer);
        }

        async ValueTask ReadExactAsync(Memory<byte> memory)
        {
            var left = memory;
            while (left.Length > 0)
            {
                var read = await inner.ReadAsync(left);
                if (read <= 0)
                {
                    throw new IOException("Channel is closed");
                }
                left = left.Slice(read);
            }
        }

        public async ValueTask WriteAsync(int length, IMemoryOwner<byte> message)
        {
            using (var payloadOwner = MemoryPool<byte>.Shared.Rent(length + 4))
            {
                var payload = payloadOwner.Memory.Slice(0, length + 4);
                payload.Span[0] = (byte)(length >> 24);
                payload.Span[1] = (byte)(length >> 16);
                payload.Span[2] = (byte)(length >> 8);
                payload.Span[3] = (byte)length;

                message.Memory.Span.Slice(0, length).CopyTo(payload.Span.Slice(4));
                message.Dispose();
                await inner.WriteAsync(payload);
            }
        }
    }

    //public sealed class Buffer : IDisposable
    //{
    //    readonly byte[] inner;
    //    int length;
    //    int disposed;

    //    public Buffer(int length)
    //    {
    //        this.length = length;
    //        if (length > 0)
    //            this.inner = BufferPool.Instance.Rent(length);
    //        else
    //            this.inner = Array.Empty<byte>();
    //    }

    //    public ArraySegment<byte> Data => new ArraySegment<byte>(this.inner, 0, length);

    //    public void Dispose()
    //    {
    //        if (this.length > 0)
    //        {
    //            if (Interlocked.Exchange(ref this.disposed, 1) == 0)
    //            {
    //                BufferPool.Instance.Return(this.inner);
    //            }
    //        }
    //    }
    //}

    //static class BufferPool
    //{
    //    public static readonly ArrayPool<byte> Instance = ArrayPool<byte>.Shared;
    //}
}
