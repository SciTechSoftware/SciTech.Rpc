using NUnit.Framework;
using SciTech.Rpc.Client;
using SciTech.Rpc.Grpc.Client;
using SciTech.Rpc.Grpc.Server;
using SciTech.Rpc.Grpc.Tests;
using SciTech.Rpc.Lightweight.Client;
using SciTech.Rpc.Lightweight.Server;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using System;
using System.IO.Pipelines;

namespace SciTech.Rpc.Tests
{
    public enum RpcConnectionType
    {
        LightweightInproc,
        LightweightTcp,
        Grpc
    }

    public class ClientServerTestsBase
    {
        internal const int TcpTestPort = 15959;

        private RpcConnectionType connectionType;

        private RpcServiceOptions options;

        protected ClientServerTestsBase(IRpcSerializer serializer, RpcConnectionType connectionType)
        {
            this.options = new RpcServiceOptions { Serializer = serializer };
            this.connectionType = connectionType;
        }

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
        /// TODO: Make this virtual instead of using this.connnectioType.
        /// </summary>
        /// <param name="serviceDefinitionsBuilder"></param>
        /// <param name="proxyServicesProvider"></param>
        /// <returns></returns>
        protected (IRpcServer, RpcServerConnection) CreateServerAndConnection(RpcServiceDefinitionBuilder serviceDefinitionsBuilder, IRpcProxyDefinitionsProvider proxyServicesProvider = null)
        {
            var rpcServerId = RpcServerId.NewId();

            switch (this.connectionType)
            {
                case RpcConnectionType.LightweightTcp:
                    {
                        var host = new LightweightRpcServer(rpcServerId, serviceDefinitionsBuilder, null, this.options);
                        host.AddEndPoint(new TcpLightweightRpcEndPoint("127.0.0.1", TcpTestPort, false));

                        var proxyGenerator = new LightweightProxyProvider(proxyServicesProvider);
                        var connection = new TcpLightweightRpcConnection(
                            new RpcServerConnectionInfo("TCP", new Uri($"lightweight.tcp://127.0.0.1:{TcpTestPort}"), rpcServerId),
                            proxyGenerator, this.options.Serializer);

                        return (host, connection);
                    }
                case RpcConnectionType.LightweightInproc:
                    {
                        Pipe requestPipe = new Pipe();
                        Pipe responsePipe = new Pipe();

                        var host = new LightweightRpcServer(rpcServerId, serviceDefinitionsBuilder, null, this.options);
                        host.AddEndPoint(new DirectLightweightRpcEndPoint(new DirectDuplexPipe(requestPipe.Reader, responsePipe.Writer)));

                        var proxyGenerator = new LightweightProxyProvider(proxyServicesProvider);
                        var connection = new DirectLightweightRpcConnection(new RpcServerConnectionInfo("Direct", new Uri("direct:localhost"), rpcServerId),
                            new DirectDuplexPipe(responsePipe.Reader, requestPipe.Writer), proxyGenerator, this.options.Serializer);
                        return (host, connection);
                    }
                case RpcConnectionType.Grpc:
                    {
                        var host = new GrpcServer(rpcServerId, serviceDefinitionsBuilder, null, this.options);
                        host.AddEndPoint(GrpcCoreFullStackTestsBase.CreateEndPoint());

                        var proxyGenerator = new GrpcProxyProvider(proxyServicesProvider);
                        var connection = new GrpcServerConnection(
                            new RpcServerConnectionInfo("TCP", new Uri($"grpc://localhost:{GrpcCoreFullStackTestsBase.GrpcTestPort}"), rpcServerId),
                            TestCertificates.SslCredentials, proxyGenerator, this.options.Serializer );
                        return (host, connection);
                    }
            }

            throw new NotSupportedException();
        }
    }

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
