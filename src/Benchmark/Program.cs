using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnostics.Windows.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.CsProj;
using Grpc.Core;
using ProtoBuf.Meta;
using SciTech.Rpc;
using SciTech.Rpc.Benchmark;
using SciTech.Rpc.Client;
using SciTech.Rpc.Grpc.Client;
using SciTech.Rpc.Grpc.Server;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Lightweight;
using SciTech.Rpc.Lightweight.Client;
using SciTech.Rpc.Lightweight.Server;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server;
using System;
using System.IO;
using System.Security.Cryptography;
using System.ServiceModel;
using System.Threading.Tasks;

namespace Benchmark
{

    public enum RpcConnectionType
    {
        LightweightInproc,
        LightweightTcp,
        LightweightNamedPipe,
        Grpc,
        NetGrpc
    }

    public class GrpcSimpleService : SciTech.Rpc.Benchmark.SimpleService.SimpleServiceBase
    {
        public override Task<AddResponse> Add(AddRequest request, ServerCallContext context)
        {
            return Task.FromResult(new AddResponse { Sum = request.A + request.B });
        }
    }

    [RpcService(IsSingleton = true,AllowFault =false)]
    // [ServiceContract]
    public interface ISimpleService
    {
        //[OperationContract]
        [RpcOperation(AllowInlineExecution = true)]
        int AddInline(int a, int b);

        [RpcOperation]
        int Add(int a, int b);
    }

    [RpcService(ServerDefinitionType = typeof(ISimpleService),IsSingleton =true,AllowFault =false)]
    public interface ISimpleServiceClient : ISimpleService
    {
        Task<int> AddAsync(int a, int b);

        Task<int> AddInlineAsync(int a, int b);
    }

    public class SimpleServiceImpl : ISimpleService
    {
        public int Add(int a, int b)
        {
            return a + b;
        }
        public int AddInline(int a, int b)
        {
            return a + b;
        }
    }

    public class MultipleRuntimes : ManualConfig
    {
        public MultipleRuntimes()
        {
            Add(Job.Default.With(CsProjCoreToolchain.NetCoreApp30)/*.WithIterationCount(3)*/);//.WithAffinity((IntPtr)7));; // .NET Core 3.0
            //Add(Job.Default.With(CsProjClassicNetToolchain.Net472).WithIterationCount(3));//.WithAffinity((IntPtr)7)); // NET 4.7.2
            //Add(Job.Default.With(CsProjClassicNetToolchain.Net472).With(Platform.X86).WithIterationCount(3));//.WithAffinity((IntPtr)7)); // NET 4.7.2

            //Add(Job.Default.With(CsProjCoreToolchain.NetCoreApp30).WithAffinity((IntPtr)3).WithIterationCount(3)); // .NET Core 2.1
            //Add(Job.Default.With(CsProjClassicNetToolchain.Net472).WithAffinity((IntPtr)3).WithIterationCount(3)); // NET 4.6.2

            //Add(Job.Default.With(CsProjCoreToolchain.NetCoreApp21));//.WithAffinity((IntPtr)7)); // .NET Core 2.1
            //Add(Job.Default.With(CsProjCoreToolchain.NetCoreApp20));//.WithAffinity((IntPtr)7)); // .NET Core 2.0
            //Add(Job.Default.With(CsProjCoreToolchain.NetCoreApp30).WithIterationCount(3));//.WithAffinity((IntPtr)7));; // .NET Core 3.0
            //Add(Job.Default.With(CsProjCoreToolchain.NetCoreApp21).WithIterationCount(3));//.WithAffinity((IntPtr)7)); // .NET Core 2.1
            //Add(Job.Default.With(CsProjCoreToolchain.NetCoreApp20).WithIterationCount(3));//.WithAffinity((IntPtr)7)); // .NET Core 2.0
            //Add(Job.Default.With(CsProjClassicNetToolchain.Net472).WithIterationCount(3));//.WithAffinity((IntPtr)7)); // NET 4.7.2
            //Add(Job.Default.With(CsProjClassicNetToolchain.Net472).With(Platform.X86).WithIterationCount(3));//.WithAffinity((IntPtr)7)); // NET 4.7.2

            //Add(Job.Default.With(CsProjCoreToolchain.NetCoreApp30).WithIterationCount(3).WithAffinity((IntPtr)3)); // .NET Core 2.1
            //Add(Job.Default.With(CsProjClassicNetToolchain.Net472).WithIterationCount(3).WithAffinity((IntPtr)3)); // NET 4.6.2


            //Add(Job.Default.With(CsProjCoreToolchain.NetCoreApp30));//.WithIterationCount(3)); // .NET Core 2.1
            //Add(Job.Default.With(CsProjCoreToolchain.NetCoreApp21));//.WithIterationCount(3)); // .NET Core 2.1
            //Add(Job.Default.With(CsProjCoreToolchain.NetCoreApp20));//.WithIterationCount(3)); // .NET Core 2.1
            //Add(Job.Default.With(CsProjClassicNetToolchain.Net472));//.WithIterationCount(3)); // NET 4.6.2
            //Add(Job.Default.With(CsProjClassicNetToolchain.Net472).With(Platform.X86));//.WithIterationCount(3)); // NET 4.6.2
        }
    }

    //[ClrJob, CoreJob, LegacyJitX86Job, ShortRunJob]
    //[ShortRunJob]
    [MemoryDiagnoser]
    //[EtwProfiler]
    [Config(typeof(MultipleRuntimes))]
    public class RawGrpcBenchmark
    {
        [Params(RpcConnectionType.Grpc)]
        public RpcConnectionType ConnectionType;

        private Channel channel;
        private SimpleService.SimpleServiceClient clientService;
        private Server server;

        [GlobalSetup]
        public void GlobalSetup()
        {
            var definitionsProvider = new RpcServiceDefinitionBuilder();

            switch (this.ConnectionType)
            {
                case RpcConnectionType.Grpc:
                    this.server = new Server
                    {
                        Services = { SimpleService.BindService(new GrpcSimpleService()) },
                        Ports = { new ServerPort("localhost", 50051, ServerCredentials.Insecure) }
                    };
                    this.server.Start();

                    this.channel = new Channel("127.0.0.1:50051", ChannelCredentials.Insecure);
                    this.clientService = new SimpleService.SimpleServiceClient(this.channel);
                    break;

            }
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            this.channel.ShutdownAsync().Wait();
            this.server.ShutdownAsync().Wait();
        }

        [Benchmark(OperationsPerInvoke = 8)]
        public AddResponse[] ParallelCalls()
        {
            Task<AddResponse>[] tasks = new Task<AddResponse>[8];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = this.clientService.AddAsync(new AddRequest { A = 5 + i, B = 6 * i }).ResponseAsync;
            }

            return Task.WhenAll(tasks).Result;
        }

        [Benchmark]
        public AddResponse SingleCall()
        {
            return this.clientService.Add(new AddRequest { A = 5, B = 6 });
        }

    }

    //[ClrJob, CoreJob, LegacyJitX86Job, ShortRunJob]
    //[ShortRunJob]
    [MemoryDiagnoser]
    //[EtwProfiler]
    [Config(typeof(MultipleRuntimes))]
    public class SimpleServiceCall
    {
        public static readonly RuntimeTypeModel DefaultTypeModel = RuntimeTypeModel.Create().AddRpcTypes();

        IRpcServer server;

        IRpcServerConnection clientConnection;
        ISimpleServiceClient clientService;

        public SimpleServiceCall()
        {

        }


        [Params(/*RpcConnectionType.Grpc,*/ RpcConnectionType.LightweightInproc/*, RpcConnectionType.LightweightTcp*/)]
        public RpcConnectionType ConnectionType;

        [GlobalSetup]
        public void GlobalSetup()
        {
            requestWriteStream = new MemoryStream(requestWriteBuffer);
            requestReadStream = new MemoryStream(requestReadBuffer);

            responseWriteStream = new MemoryStream(responseWriteBuffer);
            responseReadStream = new MemoryStream(responseReadBuffer);


            var definitionsProvider = new RpcServiceDefinitionBuilder();
            // var serializer = new JsonRpcSerializer();
            var serializer = new ProtobufRpcSerializer(RuntimeTypeModel.Create());
            var serverOptions = new RpcServerOptions { Serializer = serializer };
            var clientOptions = new RpcClientOptions { Serializer = serializer };
            switch (this.ConnectionType)
            {
                case RpcConnectionType.Grpc:
                    this.server = new GrpcServer(definitionsProvider, null, serverOptions);
                    this.server.AddEndPoint(new GrpcServerEndPoint("localhost", 50051, false, Grpc.Core.ServerCredentials.Insecure));
                    this.server.ServicePublisher.PublishSingleton<ISimpleService>(new SimpleServiceImpl());
                    this.server.Start();

                    this.clientConnection = new GrpcServerConnection(new RpcServerConnectionInfo(new Uri("grpc://localhost:50051")), clientOptions);
                    clientService = this.clientConnection.GetServiceSingleton<ISimpleServiceClient>();
                    break;
                case RpcConnectionType.LightweightInproc:
                    {
                        var connector = new DirectLightweightRpcConnector(clientOptions);
                        this.server = new LightweightRpcServer(definitionsProvider, null, serverOptions );
                        this.server.AddEndPoint(connector.EndPoint);
                        this.server.ServicePublisher.PublishSingleton<ISimpleService>(new SimpleServiceImpl());
                        this.server.Start();

                        this.clientConnection = connector.Connection;
                        clientService = this.clientConnection.GetServiceSingleton<ISimpleServiceClient>();
                        break;
                    }
                case RpcConnectionType.LightweightTcp:
                    {
                        this.server = new LightweightRpcServer(definitionsProvider, null, serverOptions);
                        this.server.AddEndPoint(new TcpLightweightRpcEndPoint("127.0.0.1", 50051, false));
                        this.server.ServicePublisher.PublishSingleton<ISimpleService>(new SimpleServiceImpl());
                        this.server.Start();

                        this.clientConnection = new TcpLightweightRpcConnection(new RpcServerConnectionInfo(new Uri("lightweight.tcp://localhost:50051")), 
                            null,
                            clientOptions);
                        clientService = this.clientConnection.GetServiceSingleton<ISimpleServiceClient>();
                        break;
                    }
            }
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            this.clientConnection.ShutdownAsync().Wait();
            this.server.ShutdownAsync().Wait();
            this.server.Dispose();
        }

        byte[] requestWriteBuffer = new byte[4096];
        byte[] requestReadBuffer = new byte[4096];
        MemoryStream requestWriteStream;
        MemoryStream requestReadStream;

        byte[] responseWriteBuffer = new byte[4096];
        byte[] responseReadBuffer = new byte[4096];
        MemoryStream responseWriteStream;
        MemoryStream responseReadStream;

        ProtobufRpcSerializer serializer = new ProtobufRpcSerializer();

        //[Benchmark]
        public int Protobuf()
        {
            //RpcObjectRequest<int, int> request = new RpcObjectRequest<int, int>(RpcObjectId.Empty, 5, 6);
            //RpcObjectRequest<int, int> request2;

            //requestWriteStream.Seek(0, SeekOrigin.Begin);
            //requestWriteStream.SetLength(0);
            //serializer.Serialize(requestWriteStream, request);

            //requestReadStream.Seek(0, SeekOrigin.Begin);
            //requestReadStream.SetLength(0);
            //requestReadStream.Write(requestWriteBuffer, 0, (int)requestWriteStream.Position);
            //requestReadStream.Seek(0, SeekOrigin.Begin);
            //request2 = serializer.FromStream<RpcObjectRequest<int, int>>(requestReadStream);

            ////(RpcObjectRequest<int, int>)DefaultTypeModel.Deserialize(requestReadStream, null, typeof(RpcObjectRequest<int, int>), requestWriteStream.Position);

            //RpcResponse<int> response = new RpcResponse<int>(11);
            //RpcResponse<int> response2;

            //responseWriteStream.Seek(0, SeekOrigin.Begin);
            //responseWriteStream.SetLength(0);
            //serializer.ToStream(responseWriteStream, response);
            ////DefaultTypeModel.Serialize(responseWriteStream, response);

            ////Buffer.BlockCopy(responseWriteBuffer, 0, responseReadBuffer, 0, (int)responseWriteStream.Position);
            //responseReadStream.Seek(0, SeekOrigin.Begin);
            //responseReadStream.SetLength(0);
            //responseReadStream.Write(responseWriteBuffer, 0, (int)responseWriteStream.Position);
            //responseReadStream.Seek(0, SeekOrigin.Begin);
            //response2 = (RpcResponse<int>)serializer.FromStream(typeof(RpcResponse<int>), responseReadStream);
            ////response2 = (RpcResponse<int>)seri.Deserialize(responseReadStream, null, typeof(RpcResponse<int>), responseWriteStream.Position);


            //return request2.Value1 + request2.Value2 + response2.Result;
            return 0;
        }

        [Benchmark]
        public int SingleCall()
        {
            return this.clientService.AddInline(5, 6 );
        }

        [Benchmark(OperationsPerInvoke = 8)]
        public int[] ParallelCalls()
        {
            Task<int>[] tasks = new Task<int>[8];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = this.clientService.AddAsync(5 + i, 6 * i);
            }

            return Task.WhenAll(tasks).Result;
        }

    }

    public class Program
    {
        public static void Main(string[] args)
        {
            var b = new SimpleServiceCall();
            b.ConnectionType = RpcConnectionType.LightweightInproc;
            b.GlobalSetup();
            //int pres = b.Protobuf();

            int res = b.SingleCall();
            res = b.SingleCall();
            int[] res2 = b.ParallelCalls();
            res2 = b.ParallelCalls();

            b.GlobalCleanup();

            var grpcBenchmark = new RawGrpcBenchmark();
            grpcBenchmark.ConnectionType = RpcConnectionType.Grpc;
            grpcBenchmark.GlobalSetup();
            AddResponse[] grpcRes = grpcBenchmark.ParallelCalls();
            grpcBenchmark.GlobalCleanup();


            var summary = BenchmarkRunner.Run<SimpleServiceCall>();
            //var summary2 = BenchmarkRunner.Run<RawGrpcBenchmark>();

            //BenchmarkRunner.Run<SimpleServiceCall>(
            //    DefaultConfig.Instance
            //    .With(Job.Default.With(CsProjCoreToolchain.NetCoreApp30)));
        }
    }
}
