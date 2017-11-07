using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using System;
using System.Threading.Tasks;

namespace NetworkingPerf
{
    public class EchoServerHandler : ChannelHandlerAdapter
    {
        //Action<Task> WriteContinuation;
        IChannelHandlerContext capturedContext;

        //public EchoServerHandler()
        //{
        //    this.WriteContinuation = t => { capturedContext.Channel.CloseAsync(); };
        //}

        //public override void ChannelActive(IChannelHandlerContext context)
        //{
        //    this.capturedContext = context;
        //    base.ChannelActive(context);
        //}

        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            var buffer = message as IByteBuffer;
            context.WriteAsync(message);//.ContinueWith(this.WriteContinuation, TaskContinuationOptions.NotOnRanToCompletion);
        }

        public override void ChannelReadComplete(IChannelHandlerContext context) => context.Flush();

        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            Console.WriteLine("Exception: " + exception);
            context.CloseAsync();
        }
    }
}
