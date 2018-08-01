using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Common;
using DotNetty.Common.Internal.Logging;
using DotNetty.Handlers.Logging;
using DotNetty.Handlers.Tls;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using DotNetty.Transport.Libuv;
using Microsoft.Extensions.Logging.Console;
using System;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.IO;

namespace NetworkingPerf
{
    class Program
    {
        static readonly int ThreadCount = Environment.ProcessorCount;

        static void Main(string[] args)
        {
            var tlsCertificate = new X509Certificate2("gateway.tests.com.pfx", "password");

            var listenSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            listenSocket.Bind(new IPEndPoint(IPAddress.Any, 10003));
            listenSocket.Listen(2000);
            Enumerable.Repeat(0, ThreadCount).Select(async (x) =>
            {
                while (listenSocket.IsBound)
                {
                    try
                    {
                        var socket = await listenSocket.AcceptAsync();
                        var ignore = Task.Run(async () =>
                        {
                            SslStream sslStream = null;
                            try
                            {
                                socket.NoDelay = true;
                                var netStream = new NetworkStream(socket);
                                //var bufferedStream = new BufferedStream(netStream, 2048);
                                sslStream = new SslStream(netStream);
                                await sslStream.AuthenticateAsServerAsync(tlsCertificate, false, SslProtocols.Tls12 | SslProtocols.Tls11, true);
                                var messenger = new Messenger(sslStream);
                                while (true)
                                {
                                    (var length, var message) = await messenger.ReadAsync();
                                    await messenger.WriteAsync(length, message);
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("Error serving connection: " + ex);
                                sslStream?.Close();
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error accepting socket: " + ex);
                    }
                }
            }).ToArray();

            //InternalLoggerFactory.DefaultFactory.AddProvider(new ConsoleLoggerProvider((s, level) => true, false));
            ResourceLeakDetector.Level = ResourceLeakDetector.DetectionLevel.Disabled;

            var dispatcher = new DispatcherEventLoopGroup();
            var bossGroup = dispatcher;
            var workerGroup = new WorkerEventLoopGroup(dispatcher);
            //var bossGroup = new MultithreadEventLoopGroup(elg => new SingleThreadEventLoop(elg, "STEL-B", TimeSpan.FromMilliseconds(200)), 1);
            //var workerGroup = new MultithreadEventLoopGroup(elg => new SingleThreadEventLoop(elg, "STEL-W", TimeSpan.FromMilliseconds(200)), ThreadCount);
            var tlsLengthEchoChannel = new ServerBootstrap()
                .Group(bossGroup, workerGroup)
                .Channel<TcpServerChannel>()
                //.Channel<TcpServerSocketChannel>()
                .Option(ChannelOption.SoBacklog, 2000)
                //.Option(ChannelOption.Allocator, PooledByteBufferAllocator.Default)
                //.ChildOption(ChannelOption.Allocator, PooledByteBufferAllocator.Default)
                .ChildOption(ChannelOption.TcpNodelay, true)
                //.Handler(new LoggingHandler("LSTN"))
                .ChildHandler(new ActionChannelInitializer<IChannel>(channel =>
                {
                    IChannelPipeline pipeline = channel.Pipeline;
                    if (tlsCertificate != null)
                    {
                        pipeline.AddLast("tls", TlsHandler.Server(tlsCertificate));
                    }
                    pipeline.AddLast("framing-enc", new LengthFieldPrepender(4));
                    pipeline.AddLast("framing-dec", new LengthFieldBasedFrameDecoder(int.MaxValue, 0, 4, 0, 4));
                    pipeline.AddLast("echo", new EchoServerHandler());
                    //pipeline.AddLast("Log", new LoggingHandler("CONN"));
                }))
                .BindAsync(IPAddress.Any, 10001).Result;

            Console.WriteLine("All listening");
            Console.ReadLine();
            tlsLengthEchoChannel.CloseAsync().Wait();
            Console.WriteLine("All done");
        }
    }
}
