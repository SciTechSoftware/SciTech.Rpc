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

#if NETCOREAPP3_0
using SciTech.Rpc.NetGrpc.Server.Internal;
using SciTech.Rpc.NetGrpc.Client;
using SciTech.Rpc.NetGrpc.Server;
using GrpcNet = Grpc.Net;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
#endif

namespace SciTech.Rpc.Tests
{
    public enum RpcConnectionType
    {
        LightweightInproc,
        LightweightTcp,
        LightweightSslTcp,
        LightweightNamedPipe,
        Grpc,
        NetGrpc
    }

    public interface ITestConnectionCreator
    {
        (IRpcServer, RpcServerConnection) CreateServerAndConnection(RpcServiceDefinitionBuilder serviceDefinitionsBuilder,
            Action<RpcServerOptions> configServerOptions = null,
            Action<RpcClientOptions> configClientOptions = null,
            IRpcProxyDefinitionsProvider proxyServicesProvider = null);
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
        /// TODO: Make this virtual instead of using this.connnectionType.
        /// </summary>
        /// <param name="serviceDefinitionsProvider"></param>
        /// <param name="proxyServicesProvider"></param>
        /// <returns></returns>
        protected (IRpcServer, RpcServerConnection) CreateServerAndConnection(IRpcServiceDefinitionsProvider serviceDefinitionsProvider,
            Action<RpcServerOptions> configServerOptions = null,
            Action<RpcClientOptions> configClientOptions = null,
            IRpcProxyDefinitionsProvider proxyServicesProvider = null)
        {
            var rpcServerId = RpcServerId.NewId();

            var options = new RpcServerOptions { Serializer = this.serializer };
            var clientOptions = new RpcClientOptions { Serializer = this.serializer };
            configServerOptions?.Invoke(options);
            configClientOptions?.Invoke(clientOptions);

            switch (this.ConnectionType)
            {
                case RpcConnectionType.LightweightTcp:
                case RpcConnectionType.LightweightSslTcp:
                    {
                        var host = new LightweightRpcServer(rpcServerId, serviceDefinitionsProvider, null, options, this.LightweightOptions);

                        SslServerOptions sslServerOptions = null;
                        if (this.ConnectionType == RpcConnectionType.LightweightSslTcp)
                        {
                            sslServerOptions = new SslServerOptions(new X509Certificate2(TestCertificates.ServerPFXPath, "1111"));
                        }

                        host.AddEndPoint(new TcpLightweightRpcEndPoint("127.0.0.1", TcpTestPort, false, sslServerOptions));

                        SslClientOptions sslClientOptions = null;
                        if (this.ConnectionType == RpcConnectionType.LightweightSslTcp)
                        {
                            sslClientOptions = new SslClientOptions { RemoteCertificateValidationCallback = this.ValidateTestCertificate };

                        }
                        var connection = new TcpLightweightRpcConnection(
                            new RpcServerConnectionInfo("TCP", new Uri($"lightweight.tcp://127.0.0.1:{TcpTestPort}"), rpcServerId),
                            sslClientOptions,
                            clientOptions.AsImmutable(),
                            proxyServicesProvider,
                            this.LightweightOptions);

                        return (host, connection);
                    }
                case RpcConnectionType.LightweightNamedPipe:
                    {
                        var server = new LightweightRpcServer(rpcServerId, serviceDefinitionsProvider, null, options, this.LightweightOptions);
                        server.AddEndPoint(new NamedPipeRpcEndPoint("testpipe"));

                        var connection = new NamedPipeRpcConnection(
                            new RpcServerConnectionInfo(new Uri("lightweight.pipe://./testpipe")),
                            clientOptions.AsImmutable(),
                            proxyServicesProvider,
                            this.LightweightOptions);

                        return (server, connection);
                    }
                case RpcConnectionType.LightweightInproc:
                    {
                        Pipe requestPipe = new Pipe(new PipeOptions(readerScheduler: PipeScheduler.Inline));
                        Pipe responsePipe = new Pipe(new PipeOptions(readerScheduler: PipeScheduler.Inline));

                        var host = new LightweightRpcServer(rpcServerId, serviceDefinitionsProvider, null, options);
                        host.AddEndPoint(new DirectLightweightRpcEndPoint(new DirectDuplexPipe(requestPipe.Reader, responsePipe.Writer)));

                        var connection = new DirectLightweightRpcConnection(new RpcServerConnectionInfo("Direct", new Uri("direct:localhost"), rpcServerId),
                            new DirectDuplexPipe(responsePipe.Reader, requestPipe.Writer), clientOptions.AsImmutable(), proxyServicesProvider);
                        return (host, connection);
                    }
                case RpcConnectionType.Grpc:
                    {
                        var host = new GrpcServer(rpcServerId, serviceDefinitionsProvider, null, options);
                        host.AddEndPoint(GrpcCoreFullStackTestsBase.CreateEndPoint());

                        var connection = new GrpcServerConnection(
                            new RpcServerConnectionInfo("TCP", new Uri($"grpc://localhost:{GrpcCoreFullStackTestsBase.GrpcTestPort}"), rpcServerId),
                            TestCertificates.GrpcSslCredentials, clientOptions.AsImmutable(), proxyServicesProvider);
                        return (host, connection);
                    }
#if NETCOREAPP3_0
                case RpcConnectionType.NetGrpc:
                    {
                        var server = CreateNetGrpcServer(serviceDefinitionsProvider, rpcServerId);
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
                            clientOptions.AsImmutable(), proxyServicesProvider, channelOptions);
                        return (server, connection);
                    }
#endif
            }

            throw new NotSupportedException();
        }


#if NETCOREAPP3_0
        private static IRpcServer CreateNetGrpcServer(IRpcServiceDefinitionsProvider serviceDefinitionsProvider, RpcServerId serverId)
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
                .ConfigureServices(s=>
                {
                    s.AddSingleton(serviceDefinitionsProvider);
                    s.Configure<RpcServicePublisherOptions>(o => o.ServerId = serverId);
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

#if NETCOREAPP3_0
    internal class NetGrpcTestServer : IRpcServer
    {
        private IWebHost webHost;
        NetGrpcServer server;

        internal NetGrpcTestServer(IWebHost webHost, NetGrpcServer server )
        {
            this.webHost = webHost;
            this.server = server;

        }
        public bool AllowAutoPublish => this.server.AllowAutoPublish;

        public ImmutableArray<RpcServerCallInterceptor> CallInterceptors => this.server.CallInterceptors;

        public IRpcServicePublisher ServicePublisher => this.server.ServicePublisher;

        public void AddEndPoint(IRpcServerEndPoint endPoint)
        {
            
        }

        public void Dispose()
        {
            this.server.Dispose();
            this.webHost.Dispose();
        }

        public async Task ShutdownAsync()
        {
            await this.server.ShutdownAsync().ConfigureAwait(false);
            await this.webHost.StopAsync().ConfigureAwait(false);
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
