﻿using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
//using BenchmarkDotNet.Diagnostics.Windows.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.CsProj;
using GrpcCore = Grpc.Core;
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
using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.ServiceModel;
using System.Threading.Tasks;
using SciTech.Collections.Immutable;

#if COREFX
using GrpcNet = Grpc.Net;
using Grpc.Net.Client;
using SciTech.Rpc.NetGrpc.Client;
using SciTech.Rpc.NetGrpc.Server;
using SciTech.Rpc.NetGrpc.Server.Internal;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
#endif

namespace SciTech.Rpc.Benchmark
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
        public override Task<AddResponse> Add(AddRequest request, GrpcCore.ServerCallContext context)
        {
            return Task.FromResult(new AddResponse { Sum = request.A + request.B });
        }
    }

    [RpcService(IsSingleton = true)]
    // [ServiceContract]
    public interface ISimpleService
    {
        //[OperationContract]
        [RpcOperation(AllowInlineExecution = true)]
        int AddInline(int a, int b);

        [RpcOperation]
        int Add(int a, int b);
    }

    [RpcService(ServerDefinitionType = typeof(ISimpleService),IsSingleton =true)]
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
            AddJob(Job.Default.WithToolchain(CsProjCoreToolchain.NetCoreApp50)/*.WithIterationCount(3)*/);//.WithAffinity((IntPtr)7));; // .NET Core 3.0
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
    //[MemoryDiagnoser]
    //[EtwProfiler]
    [Config(typeof(MultipleRuntimes))]
    public class RawGrpcBenchmark
    {
        [Params(RpcConnectionType.Grpc, RpcConnectionType.NetGrpc)]
        public RpcConnectionType ConnectionType;

        private GrpcCore.ChannelBase channel;
        private SimpleService.SimpleServiceClient clientService;
        private GrpcCore.Server server;
#if COREFX
        private IWebHost host;
#endif

        [GlobalSetup]
        public void GlobalSetup()
        {
            var definitionsProvider = new RpcServiceDefinitionsBuilder();

            switch (this.ConnectionType)
            {
                case RpcConnectionType.Grpc:
                    this.server = new GrpcCore.Server
                    {
                        Services = { SimpleService.BindService(new GrpcSimpleService()) },
                        Ports = { new GrpcCore.ServerPort("localhost", 50051, GrpcCore.ServerCredentials.Insecure) }
                    };
                    this.server.Start();

                    this.channel = new GrpcCore.Channel("127.0.0.1:50051", GrpcCore.ChannelCredentials.Insecure);
                    this.clientService = new SimpleService.SimpleServiceClient(this.channel);
                    break;
#if COREFX
                case RpcConnectionType.NetGrpc:
                    this.host = CreateNetGrpcHost();
                    host.Start();


                    var handler = new System.Net.Http.HttpClientHandler();
                    handler.ServerCertificateCustomValidationCallback =
                        (httpRequestMessage, cert, cetChain, policyErrors) =>
                        {
                            return true;
                        };
                    var channelOptions = new GrpcNet.Client.GrpcChannelOptions()
                    {
                        HttpClient = new System.Net.Http.HttpClient(handler),
                        DisposeHttpClient = true
                    };

                    this.channel = GrpcChannel.ForAddress("https://localhost:50051", channelOptions);
                    this.clientService = new SimpleService.SimpleServiceClient(channel);
                    break;
#endif      
            }
        }

#if COREFX
        private static IWebHost CreateNetGrpcHost()
        {
            var hostBuilder = new WebHostBuilder()
                .UseKestrel()
                .ConfigureKestrel(options =>
                {
                    options.ListenLocalhost(50051, listenOptions =>
                    {
                        listenOptions.UseHttps(TestCertificates.ServerPFXPath, "1111");
                        listenOptions.Protocols = HttpProtocols.Http2;
                    });
                })
                .ConfigureServices(s =>
                {
                })
                .UseStartup<NetStartup>();

            var host = hostBuilder.Build();

            return host;

        }

        public class NetStartup
        {
            public void Configure(IApplicationBuilder app)
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGrpcService<GrpcSimpleService>();
                });
            }

            public void ConfigureServices(IServiceCollection services)
            {
                services.AddNetGrpc();
            }
        }
#endif

        [GlobalCleanup]
        public void GlobalCleanup()
        {
#if COREFX
            if (this.ConnectionType == RpcConnectionType.NetGrpc)
            {
                this.host.StopAsync().Wait();
            }
#endif
            if (this.ConnectionType == RpcConnectionType.Grpc)
            {
                ((GrpcCore.Channel)this.channel).ShutdownAsync().Wait();
                this.server.ShutdownAsync().Wait();
            }
        }

        Task<AddResponse>[] tasks = new Task<AddResponse>[8];

        [Benchmark(OperationsPerInvoke = 8)]
        public AddResponse[] ParallelCalls()
        {
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

    [SimpleJob()]
    public class ConnectionBenchMark
    {
        LightweightRpcServer server;
        ImmutableRpcClientOptions clientOptions;
        RpcConnectionInfo connectionInfo;

        [Params(RpcConnectionType.LightweightTcp, RpcConnectionType.LightweightNamedPipe)]
        public RpcConnectionType ConnectionType;

        [GlobalSetup]
        public void GlobalSetup()
        {
            var serverId = RpcServerId.NewId();
            var definitionsProvider = new RpcServiceDefinitionsBuilder();
            // var serializer = new JsonRpcSerializer();
            var serializer = new ProtobufRpcSerializer(RuntimeTypeModel.Create());
            var serverOptions = new RpcServerOptions { Serializer = serializer };
            this.clientOptions = new RpcClientOptions { Serializer = serializer }.AsImmutable();

            switch (this.ConnectionType)
            {
                //case RpcConnectionType.LightweightInproc:
                //    {
                //        var connector = new InprocRpcConnector(clientOptions);
                //        this.server = new LightweightRpcServer(serverId, definitionsProvider, null, serverOptions);
                //        this.server.AddEndPoint(connector.EndPoint);
                //        this.server.ServicePublisher.PublishSingleton<ISimpleService>(new SimpleServiceImpl());
                //        this.server.Start();

                //        //this.clientConnection = connector.Connection;
                //        //clientService = this.clientConnection.GetServiceSingleton<ISimpleServiceClient>();
                //        break;
                //    }
                case RpcConnectionType.LightweightTcp:
                    {
                        this.server = new LightweightRpcServer(serverId, definitionsProvider, null, serverOptions);
                        this.server.AddEndPoint(new TcpRpcEndPoint("127.0.0.1", 50051, false));
                        this.server.ServicePublisher.PublishSingleton<ISimpleService>(new SimpleServiceImpl());
                        this.server.Start();

                        this.connectionInfo = new RpcConnectionInfo(new Uri("lightweight.tcp://localhost:50051"));
                        break;
                    }
                case RpcConnectionType.LightweightNamedPipe:
                    {
                        this.server = new LightweightRpcServer(serverId, definitionsProvider, null, serverOptions);
                        this.server.AddEndPoint(new NamedPipeRpcEndPoint("RpcBenchmark"));
                        this.server.ServicePublisher.PublishSingleton<ISimpleService>(new SimpleServiceImpl());
                        this.server.Start();

                        this.connectionInfo = new RpcConnectionInfo(new Uri($"{WellKnownRpcSchemes.LightweightPipe}://./RpcBenchmark"));
                        break;
                    }
            }
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            this.server.ShutdownAsync().Wait();
            this.server.Dispose();
        }

        [Benchmark]
        public void ConnectDisconnect()
        {
            IRpcConnection connection;
            switch (this.ConnectionType)
            {
                case RpcConnectionType.LightweightTcp:
                    connection = new TcpRpcConnection(this.connectionInfo, options: this.clientOptions);
                    break;
                case RpcConnectionType.LightweightNamedPipe:
                    connection = new NamedPipeRpcConnection(this.connectionInfo, this.clientOptions);
                    break;
                default:
                    throw new InvalidOperationException();

            }
            
            connection.ConnectAsync(default).Wait();

            var service = connection.GetServiceSingleton<ISimpleService>(false);
            service.AddInline(1, 2);

            connection.ShutdownAsync().Wait();
        }



    }

    //[ClrJob, CoreJob, LegacyJitX86Job, ShortRunJob]
    //[ShortRunJob]
    //[MemoryDiagnoser]
    //[EtwProfiler]
    //[Config(typeof(MultipleRuntimes))]
    [SimpleJob()]
    [MarkdownExporterAttribute.GitHub]
    public class SimpleServiceCallBenchmark
    {
        public static readonly RuntimeTypeModel DefaultTypeModel = RuntimeTypeModel.Create().AddRpcTypes();

        IRpcServerHost server;

        IRpcChannel clientConnection;
        IRpcChannel[] clientConnections;
        ISimpleServiceClient clientService;
        ISimpleServiceClient[] clientServices;

        public SimpleServiceCallBenchmark()
        {

        }

        const int MaxClientConnections = 8;

#if COREFX
        //[Params(RpcConnectionType.LightweightTcp, RpcConnectionType.LightweightNamedPipe)]
        [Params(RpcConnectionType.Grpc, RpcConnectionType.NetGrpc, RpcConnectionType.LightweightTcp, RpcConnectionType.LightweightInproc, RpcConnectionType.LightweightNamedPipe)]
#else
        [Params(/*RpcConnectionType.Grpc,*/ /*, RpcConnectionType.LightweightTcp*/)]
#endif
        public RpcConnectionType ConnectionType;

        [GlobalSetup]
        public void GlobalSetup()
        {
            requestWriteStream = new MemoryStream(requestWriteBuffer);
            requestReadStream = new MemoryStream(requestReadBuffer);

            responseWriteStream = new MemoryStream(responseWriteBuffer);
            responseReadStream = new MemoryStream(responseReadBuffer);


            var serverId = RpcServerId.NewId();
            var definitionsProvider = new RpcServiceDefinitionsBuilder();
            // var serializer = new JsonRpcSerializer();
            var serializer = new ProtobufRpcSerializer(RuntimeTypeModel.Create());
            var serverOptions = new RpcServerOptions { Serializer = serializer };
            var clientOptions = new RpcClientOptions { Serializer = serializer };

            this.clientConnections = new IRpcChannel[MaxClientConnections];

            switch (this.ConnectionType)
            {
                case RpcConnectionType.Grpc:
                    this.server = new GrpcServer(definitionsProvider, null, serverOptions);
                    this.server.AddEndPoint(new GrpcServerEndPoint("localhost", 50051, false, GrpcCore.ServerCredentials.Insecure));
                    this.server.ServicePublisher.PublishSingleton<ISimpleService>(new SimpleServiceImpl());
                    this.server.Start();

                    for( int i=0; i < MaxClientConnections;i++ )
                    {
                        this.clientConnections[i] = new GrpcConnection(new RpcConnectionInfo(new Uri("grpc://localhost:50051")), clientOptions);
                    }

                    break;
                case RpcConnectionType.LightweightInproc:
                    {
                        var connector = new InprocRpcConnector(clientOptions);
                        this.server = new LightweightRpcServer(serverId, definitionsProvider, null, serverOptions );
                        this.server.AddEndPoint(connector.EndPoint);
                        this.server.ServicePublisher.PublishSingleton<ISimpleService>(new SimpleServiceImpl());
                        this.server.Start();

                        for (int i = 0; i < MaxClientConnections; i++)
                        {
                            this.clientConnections[i] = connector.Connection;
                        }
                        break;
                    }
                case RpcConnectionType.LightweightTcp:
                    {
                        this.server = new LightweightRpcServer(serverId, definitionsProvider, null, serverOptions);
                        this.server.AddEndPoint(new TcpRpcEndPoint("127.0.0.1", 50051, false));
                        this.server.ServicePublisher.PublishSingleton<ISimpleService>(new SimpleServiceImpl());
                        this.server.Start();

                        for (int i = 0; i < MaxClientConnections; i++)
                        {
                            this.clientConnections[i] = new TcpRpcConnection(new RpcConnectionInfo(new Uri("lightweight.tcp://localhost:50051")),
                            null,
                            clientOptions);
                        }

                        break;
                    }
                case RpcConnectionType.LightweightNamedPipe:
                    {
                        this.server = new LightweightRpcServer(serverId, definitionsProvider, null, serverOptions);
                        this.server.AddEndPoint(new NamedPipeRpcEndPoint("RpcBenchmark"));
                        this.server.ServicePublisher.PublishSingleton<ISimpleService>(new SimpleServiceImpl());
                        this.server.Start();

                        for (int i = 0; i < MaxClientConnections; i++)
                        {
                            this.clientConnections[i] = new NamedPipeRpcConnection("RpcBenchmark", clientOptions);
                        }
                        break;
                    }
#if COREFX
                case RpcConnectionType.NetGrpc:
                    {
                        this.server = CreateNetGrpcServer(definitionsProvider, serverId, serverOptions);
                        this.server.ServicePublisher.PublishSingleton<ISimpleService>(new SimpleServiceImpl());
                        this.server.Start();
                        //var host = new GrpcServer(rpcServerId, serviceDefinitionsBuilder, null, options);
                        //host.AddEndPoint(GrpcCoreFullStackTestsBase.CreateEndPoint());

                        var handler = new System.Net.Http.HttpClientHandler();
                        handler.ServerCertificateCustomValidationCallback =
                            (httpRequestMessage, cert, cetChain, policyErrors) =>
                            {
                                return true;
                            };
                        var channelOptions = new GrpcNet.Client.GrpcChannelOptions()
                        {
                            HttpClient = new System.Net.Http.HttpClient(handler),
                            DisposeHttpClient = true
                        };


                        for (int i = 0; i < MaxClientConnections; i++)
                        {
                            this.clientConnections[i] = new NetGrpcConnection(
                            new RpcConnectionInfo("net-grpc", new Uri($"grpc://localhost:{50051}"), serverId),
                            clientOptions.AsImmutable(),
                            channelOptions);
                        }
                        break;
                    }
#endif
            }

            this.clientServices = new ISimpleServiceClient[MaxClientConnections];
            for (int i = 0; i < MaxClientConnections; i++)
            {
                this.clientServices[i] = this.clientConnections[i].GetServiceSingleton<ISimpleServiceClient>();
            }

            this.clientConnection = clientConnections[0];
            this.clientService = clientServices[0];
        }


#if COREFX
        private static IRpcServerHost CreateNetGrpcServer(IRpcServiceDefinitionsProvider serviceDefinitionsProvider, RpcServerId serverId, RpcServerOptions options)
        {
            var hostBuilder = new WebHostBuilder()
                .UseKestrel()
                .ConfigureKestrel(options =>
                {
                    options.ListenLocalhost(50051, listenOptions =>
                    {
                        listenOptions.UseHttps(TestCertificates.ServerPFXPath, "1111");
                        //listenOptions.UseHttps(certPath, "1111", o =>
                        //{
                        //    o.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
                        //});
                        listenOptions.Protocols = HttpProtocols.Http2;
                    });
                })
                .ConfigureServices(s =>
                {
                    s.AddSingleton(serviceDefinitionsProvider);
                    s.Configure<RpcServicePublisherOptions>(o => o.ServerId = serverId);
                    s.AddSingleton<IOptions<RpcServerOptions>>(new OptionsWrapper<RpcServerOptions>(options));
                })
                .UseStartup<NetStartup>();

            var host = hostBuilder.Build();

            var rpcServer = (NetGrpcServer)host.Services.GetService(typeof(NetGrpcServer));

            return new NetGrpcTestServer(host, rpcServer);
        }

        public class NetStartup
        {
            public void Configure(IApplicationBuilder app)
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapNetGrpcServices();
                });
            }

            public void ConfigureServices(IServiceCollection services)
            {
                services.AddNetGrpc();
            }
        }
#endif

#if COREFX
        internal class NetGrpcTestServer : IRpcServerHost
        {
            private IWebHost webHost;
            NetGrpcServer server;

            internal NetGrpcTestServer(IWebHost webHost, NetGrpcServer server)
            {
                this.webHost = webHost;
                this.server = server;

            }
            public bool AllowAutoPublish => this.server.AllowAutoPublish;

            public ImmutableArrayList<RpcServerCallInterceptor> CallInterceptors => this.server.CallInterceptors;

            public IRpcServicePublisher ServicePublisher => this.server.ServicePublisher;

            public RpcServerId ServerId => server.ServerId;

            public void AddEndPoint(IRpcServerEndPoint endPoint)
            {

            }

            public void Dispose()
            {
                this.server.Dispose();
                this.webHost.Dispose();
            }

            public ValueTask DisposeAsync()
            {
                return new ValueTask(this.webHost.StopAsync());
            }

            public async Task ShutdownAsync()
            {
                await this.webHost.StopAsync().ConfigureAwait(false);
            }

            public void Start()
            {
                this.webHost.Start();
            }
        }

#endif

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
        public int SingleCallInline()
        {
            return this.clientService.AddInline(5, 6);
        }

        [Benchmark]
        public int SingleCall()
        {
            return this.clientService.Add(5, 6);
        }



        Task<int>[] tasks = new Task<int>[8];

        [Benchmark(OperationsPerInvoke = 8)]
        public void ParallelCalls()
        {
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = this.clientService.AddAsync(5 + i, 6 * i);
            }

            Task.WhenAll(tasks).Wait();
        }

        [Benchmark(OperationsPerInvoke = 8)]
        public int[] ParallelCalls2()
        {
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = this.clientServices[i].AddAsync(5 + i, 6 * i);
            }

            return Task.WhenAll(tasks).Result;
        }

        [Benchmark(OperationsPerInvoke = 8)]
        public int[] ParallelCallsInline2()
        {
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = this.clientServices[i].AddInlineAsync(5 + i, 6 * i);
            }

            return Task.WhenAll(tasks).Result;
        }
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            var b = new SimpleServiceCallBenchmark();
            b.ConnectionType = RpcConnectionType.NetGrpc;
            b.GlobalSetup();
            int[] res2 = b.ParallelCalls2();
            int[] res3 = b.ParallelCallsInline2();
            //b.ConnectDisconnect();
            //b.ConnectDisconnect();
            b.GlobalCleanup();
            //int pres = b.Protobuf();

            //int res = b.SingleCall();
            //res = b.SingleCall();
            //int[] res2 = b.ParallelCalls2();
            //res2 = b.ParallelCalls();

            //b.GlobalCleanup();

            var grpcBenchmark = new RawGrpcBenchmark();
            grpcBenchmark.ConnectionType = RpcConnectionType.NetGrpc;
            grpcBenchmark.GlobalSetup();
            AddResponse[] grpcRes = grpcBenchmark.ParallelCalls();
            grpcBenchmark.GlobalCleanup();


            var summary = BenchmarkRunner.Run<ConnectionBenchMark>();
            var summary2 = BenchmarkRunner.Run<SimpleServiceCallBenchmark>();
            var summary3 = BenchmarkRunner.Run<RawGrpcBenchmark>();

            //BenchmarkRunner.Run<SimpleServiceCall>(
            //    DefaultConfig.Instance
            //    .With(Job.Default.With(CsProjCoreToolchain.NetCoreApp30)));
        }
    }
}
