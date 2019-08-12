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
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace SciTech.Rpc.Tests
{
    public enum RpcConnectionType
    {
        LightweightInproc,
        LightweightTcp,
        LightweightSslTcp,
        Grpc
    }

    public class ClientServerTestsBase
    {
        internal const int TcpTestPort = 15959;

        private readonly IRpcSerializer serializer;


        protected ClientServerTestsBase(IRpcSerializer serializer, RpcConnectionType connectionType)
        {
            this.serializer = serializer;
            this.ConnectionType = connectionType;
        }

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
        /// TODO: Make this virtual instead of using this.connnectioType.
        /// </summary>
        /// <param name="serviceDefinitionsBuilder"></param>
        /// <param name="proxyServicesProvider"></param>
        /// <returns></returns>
        protected (IRpcServer, RpcServerConnection) CreateServerAndConnection(RpcServiceDefinitionBuilder serviceDefinitionsBuilder, Action<RpcServerOptions> configOptions = null, IRpcProxyDefinitionsProvider proxyServicesProvider = null )
        {
            var rpcServerId = RpcServerId.NewId();

            var options = new RpcServerOptions { Serializer = this.serializer };
            var clientOptions = new RpcClientOptions { Serializer = this.serializer };
            configOptions?.Invoke(options);

            switch (this.ConnectionType)
            {
                case RpcConnectionType.LightweightTcp:
                case RpcConnectionType.LightweightSslTcp:
                    {
                        var host = new LightweightRpcServer(rpcServerId, serviceDefinitionsBuilder, null, options);

                        SslServerOptions sslServerOptions = null;
                        if ( this.ConnectionType == RpcConnectionType.LightweightSslTcp)
                        {
                            sslServerOptions = new SslServerOptions(new X509Certificate2(TestCertificates.ServerPFXPath, "1111"));
                        }

                        host.AddEndPoint(new TcpLightweightRpcEndPoint("127.0.0.1", TcpTestPort, false, sslServerOptions));

                        SslClientOptions sslClientOptions = null;
                        if(this.ConnectionType == RpcConnectionType.LightweightSslTcp)
                        {
                            sslClientOptions = new SslClientOptions { RemoteCertificateValidationCallback = ValidateTestCertificate };

                        }
                        var proxyGenerator = new LightweightProxyProvider(proxyServicesProvider);
                        var connection = new TcpLightweightRpcConnection(
                            new RpcServerConnectionInfo("TCP", new Uri($"lightweight.tcp://127.0.0.1:{TcpTestPort}"), rpcServerId),
                            sslClientOptions, 
                            clientOptions.AsImmutable(),
                            proxyGenerator);

                        return (host, connection);
                    }
                case RpcConnectionType.LightweightInproc:
                    {
                        Pipe requestPipe = new Pipe();
                        Pipe responsePipe = new Pipe();

                        var host = new LightweightRpcServer(rpcServerId, serviceDefinitionsBuilder, null, options);
                        host.AddEndPoint(new DirectLightweightRpcEndPoint(new DirectDuplexPipe(requestPipe.Reader, responsePipe.Writer)));

                        var proxyGenerator = new LightweightProxyProvider(proxyServicesProvider);
                        var connection = new DirectLightweightRpcConnection(new RpcServerConnectionInfo("Direct", new Uri("direct:localhost"), rpcServerId),
                            new DirectDuplexPipe(responsePipe.Reader, requestPipe.Writer), clientOptions.AsImmutable(), proxyGenerator);
                        return (host, connection);
                    }
                case RpcConnectionType.Grpc:
                    {
                        var host = new GrpcServer(rpcServerId, serviceDefinitionsBuilder, null, options);
                        host.AddEndPoint(GrpcCoreFullStackTestsBase.CreateEndPoint());

                        var proxyGenerator = new GrpcProxyProvider(proxyServicesProvider);
                        var connection = new GrpcServerConnection(
                            new RpcServerConnectionInfo("TCP", new Uri($"grpc://localhost:{GrpcCoreFullStackTestsBase.GrpcTestPort}"), rpcServerId),
                            TestCertificates.GrpcSslCredentials, clientOptions.AsImmutable(), proxyGenerator );
                        return (host, connection);
                    }
            }

            throw new NotSupportedException();
        }

        bool ValidateTestCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;

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
