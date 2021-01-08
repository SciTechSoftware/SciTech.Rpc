using NUnit.Framework;
using SciTech.Rpc.Client;
using SciTech.Rpc.Grpc.Client;
using SciTech.Rpc.Grpc.Server;
using SciTech.Rpc.Lightweight;
using SciTech.Rpc.Lightweight.Client;
using SciTech.Rpc.Lightweight.Server;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using System;
using System.IO.Pipelines;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Immutable;
using System.Threading.Tasks;
using SciTech.Rpc.Tests.Grpc;
using SciTech.Rpc.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using SciTech.Collections.Immutable;
using Microsoft.Extensions.Logging;
using SciTech.Threading;

#if PLAT_NET_GRPC
using SciTech.Rpc.NetGrpc.Server.Internal;
using SciTech.Rpc.NetGrpc.Client;
using SciTech.Rpc.NetGrpc.Server;
using GrpcNet = Grpc.Net;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Builder;
#endif

namespace SciTech.Rpc.Tests
{
    public enum RpcConnectionType
    {
        LightweightInproc,
        LightweightTcp,
        LightweightSslTcp,
        LightweightNegotiateTcp,
        LightweightNamedPipe,
        Grpc,
        NetGrpc
    }

    public interface ITestConnectionCreator
    {
        (IRpcServer, RpcServerConnection) CreateServerAndConnection(RpcServiceDefinitionsBuilder serviceDefinitionsBuilder,
            Action<RpcServerOptions> configServerOptions = null,
            Action<RpcClientOptions> configClientOptions = null);
    }

    public class ClientServerTestsBase
    {
        internal const int TcpTestPort = 15959;

        private readonly IRpcSerializer serializer;

        protected ClientServerTestsBase(IRpcSerializer serializer, RpcConnectionType connectionType)
        {
            this.serializer = serializer;
            //switch( connectionType )
            //{
            //    case RpcConnectionType.LightweightInproc:
            //    case RpcConnectionType.LightweightTcp:
            //    case RpcConnectionType.LightweightSslTcp:
            //        this.connectionProvider = new LightweightConnectionCreator(connectionType);
            //        break;
            //}
            this.ConnectionType = connectionType;
        }

        /// <summary>
        /// HACK; Try to find a better way of creating test server and connection.
        /// </summary>
        public virtual LightweightOptions LightweightOptions { get; protected set; }

        protected RpcConnectionType ConnectionType { get; }

        [TearDown]
        public void Cleanup()
        {
            RpcStubOptions.TestDelayEventHandlers = false;
        }

        [SetUp]
        public void Init()
        {
            RpcStubOptions.TestDelayEventHandlers = true;
        }

        /// <summary>
        /// TODO: Use factories instead of using this.connnectionType.
        /// </summary>
        /// <param name="serviceDefinitionsProvider"></param>
        /// <param name="proxyDefinitionsProvider"></param>
        /// <returns></returns>
        protected (IRpcServerHost, IRpcChannel) CreateServerAndConnection(
            IRpcServiceDefinitionsProvider serviceDefinitionsProvider = null,
            Action<RpcServerOptions> configServerOptions = null,
            Action<RpcClientOptions> configClientOptions = null,
            Action<IServiceCollection> configureServices = null )
        {
            var rpcServerId = RpcServerId.NewId();

            var serverOptions = new RpcServerOptions { Serializer = this.serializer };
            var clientOptions = new RpcClientOptions { Serializer = this.serializer };

            configServerOptions?.Invoke(serverOptions);
            configClientOptions?.Invoke(clientOptions);

            IServiceProvider services = GetServiceProvider(configureServices);

            switch (this.ConnectionType)
            {
                case RpcConnectionType.LightweightTcp:
                case RpcConnectionType.LightweightSslTcp:
                    {
                        var host = new LightweightRpcServer(rpcServerId, serviceDefinitionsProvider, services, serverOptions, this.LightweightOptions, 
                            logger: services.GetService<ILogger<LightweightRpcServer>>());

                        SslServerOptions sslServerOptions = null;
                        if (this.ConnectionType == RpcConnectionType.LightweightSslTcp)
                        {
                            sslServerOptions = new SslServerOptions(new X509Certificate2(TestCertificates.ServerPFXPath, "1111"));
                        }

                        host.AddEndPoint(new TcpRpcEndPoint("127.0.0.1", TcpTestPort, false, sslServerOptions ));

                        SslClientOptions sslClientOptions = null;
                        if (this.ConnectionType == RpcConnectionType.LightweightSslTcp)
                        {
                            sslClientOptions = new SslClientOptions { RemoteCertificateValidationCallback = this.ValidateTestCertificate };

                        }
                        var connection = new TcpRpcConnection(
                            new RpcServerConnectionInfo("TCP", new Uri($"lightweight.tcp://127.0.0.1:{TcpTestPort}"), rpcServerId),
                            sslClientOptions,
                            clientOptions.AsImmutable(),
                            this.LightweightOptions);

                        return (host, connection);
                    }
                case RpcConnectionType.LightweightNamedPipe:
                    {
                        var server = new LightweightRpcServer(rpcServerId, serviceDefinitionsProvider, services, serverOptions, this.LightweightOptions,
                            logger: services.GetService<ILogger<LightweightRpcServer>>() );
                        server.AddEndPoint(new NamedPipeRpcEndPoint("testpipe"));

                        var connection = new NamedPipeRpcConnection(
                            new RpcServerConnectionInfo(new Uri("lightweight.pipe://./testpipe")),
                            clientOptions.AsImmutable(),
                            this.LightweightOptions);

                        return (server, connection);
                    }
                case RpcConnectionType.LightweightInproc:
                    {
                        Pipe requestPipe = new Pipe(new PipeOptions(readerScheduler: PipeScheduler.ThreadPool));
                        Pipe responsePipe = new Pipe(new PipeOptions(readerScheduler: PipeScheduler.Inline));

                        var host = new LightweightRpcServer(rpcServerId, serviceDefinitionsProvider, services, serverOptions,
                            logger: services.GetService<ILogger<LightweightRpcServer>>());
                        host.AddEndPoint(new InprocRpcEndPoint(new DirectDuplexPipe(requestPipe.Reader, responsePipe.Writer)));

                        var connection = new InprocRpcConnection(new RpcServerConnectionInfo("Direct", new Uri("direct:localhost"), rpcServerId),
                            new DirectDuplexPipe(responsePipe.Reader, requestPipe.Writer), clientOptions.AsImmutable());
                        return (host, connection);
                    }
                case RpcConnectionType.Grpc:
                    {
                        var host = new GrpcServer(rpcServerId, serviceDefinitionsProvider, services, serverOptions);
                        host.AddEndPoint(GrpcCoreFullStackTestsBase.CreateEndPoint());

                        var connection = new GrpcServerConnection(
                            new RpcServerConnectionInfo("TCP", new Uri($"grpc://localhost:{GrpcCoreFullStackTestsBase.GrpcTestPort}"), rpcServerId),
                            TestCertificates.GrpcSslCredentials, clientOptions.AsImmutable());
                        return (host, connection);
                    }
#if PLAT_NET_GRPC
                case RpcConnectionType.NetGrpc:
                    {
                        var server = CreateNetGrpcServer(serviceDefinitionsProvider, rpcServerId, serverOptions, configureServices);
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

                            
                        var connection = new NetGrpcServerConnection(
                            new RpcServerConnectionInfo("net-grpc", new Uri($"grpc://localhost:{GrpcCoreFullStackTestsBase.GrpcTestPort}"), rpcServerId),
                            clientOptions.AsImmutable(), channelOptions);
                        return (server, connection);
                    }
#endif
            }

            throw new NotSupportedException();
        }

        private static IServiceProvider GetServiceProvider(Action<IServiceCollection> configureServices)
        {
            IServiceProvider services = null;
            var serviceBuilder = new ServiceCollection();

            serviceBuilder.AddLogging();
            var loggingBuilder = new LoggingBuilder(serviceBuilder);

            ConfigureLogging(loggingBuilder);

            if (configureServices != null)
            {
                configureServices(serviceBuilder);
            }

            services = serviceBuilder.BuildServiceProvider();

            return services;
        }

        private static void ConfigureLogging(ILoggingBuilder loggingBuilder)
        {
            loggingBuilder.AddDebug();
            loggingBuilder.AddSimpleConsole(o => o.SingleLine = true);
        }

        private class LoggingBuilder : ILoggingBuilder
        {
            internal LoggingBuilder(IServiceCollection services)
            {
                this.Services = services;
            }

            public IServiceCollection Services { get; }
        }

#if PLAT_NET_GRPC
        private static IRpcServerHost CreateNetGrpcServer(
            IRpcServiceDefinitionsProvider serviceDefinitionsProvider,
            RpcServerId serverId,
            RpcServerOptions options,
            Action<IServiceCollection> configureServices)
        {
            var hostBuilder = WebHost.CreateDefaultBuilder()
                .ConfigureKestrel(options =>
                {
                    options.ListenLocalhost(GrpcCoreFullStackTestsBase.GrpcTestPort, listenOptions =>
                    {
                        listenOptions.UseHttps(TestCertificates.ServerPFXPath, "1111");
                        //listenOptions.UseHttps(certPath, "1111", o =>
                        //{
                        //    o.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
                        //});
                        listenOptions.Protocols = HttpProtocols.Http2;
                    });
                })
                .ConfigureLogging(b=>ConfigureLogging(b))
                .ConfigureServices(s =>
                {
                    s.AddSingleton(serviceDefinitionsProvider ?? new RpcServiceDefinitionsBuilder());
                    s.Configure<RpcServicePublisherOptions>(o => o.ServerId = serverId);
                    s.AddSingleton<IOptions<RpcServerOptions>>(new OptionsWrapper<RpcServerOptions>(options));

                    configureServices?.Invoke(s);
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

        private bool ValidateTestCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;

        }
    }

#if PLAT_NET_GRPC
    internal class NetGrpcTestServer : IRpcServerHost
    {
        private IWebHost webHost;
        NetGrpcServer server;

        internal NetGrpcTestServer(IWebHost webHost, NetGrpcServer server )
        {
            this.webHost = webHost;
            this.server = server;

        }
        public bool AllowAutoPublish => this.server.AllowAutoPublish;

        public ImmutableArrayList<RpcServerCallInterceptor> CallInterceptors => this.server.CallInterceptors;

        public IRpcServicePublisher ServicePublisher => this.server.ServicePublisher;

        public RpcServerId ServerId => this.server.ServicePublisher.ServerId;

        public void AddEndPoint(IRpcServerEndPoint endPoint)
        {
            
        }

        public void Dispose()
        {
            this.server.Dispose();
            this.webHost.Dispose();

        }

        public async ValueTask DisposeAsync()
        {
            await this.server.DisposeAsync().ContextFree();
            this.webHost.Dispose();
        }

        public async Task ShutdownAsync()
        {
            await this.webHost.StopAsync().ContextFree();
        }

        public void Start()
        {
            this.webHost.Start();
        }
    }

#endif
    public sealed class DirectDuplexPipe : IDuplexPipe, IDisposable
    {
        public DirectDuplexPipe(PipeReader input, PipeWriter output)
        {
            this.Input = input;
            this.Output = output;
        }

        public PipeReader Input { get; }

        public PipeWriter Output { get; }

        public void Dispose()
        {
            this.Input?.Complete();
            this.Output?.Complete();
        }
    }
}
