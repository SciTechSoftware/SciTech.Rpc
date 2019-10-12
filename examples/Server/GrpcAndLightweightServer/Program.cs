using Greeter;
using Mailer;
using Microsoft.Extensions.DependencyInjection;
using SciTech.Rpc;
using SciTech.Rpc.Grpc.Server;
using SciTech.Rpc.Lightweight.Server;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server;
using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using GrpcCore = Grpc.Core;

namespace GrpcAndLightweightServer
{
    internal class Program
    {
        public static async Task Main(string[] args)
        {
            // This example shows how to explicitly setup a gRPC RPC server and a lightweight RPC server
            // with a common service publisher.
            // 
            // In a real scenario, it is probably more suitable to use the .NET generic host
            // (https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/generic-host).
            //
            // The NetGrpcServer example shows what a host setup could look like.


            Console.WriteLine("Initializing gRPC server and lightweight RPC server.");
            var serviceCollection = new ServiceCollection();

            ConfigureServices(serviceCollection);

            var serviceProvider = serviceCollection.BuildServiceProvider();

            var definitionsBuilder = new RpcServiceDefinitionsBuilder();
            var rpcPublisher = new RpcServicePublisher(definitionsBuilder);

            RegisterServiceDefinitions(definitionsBuilder);
            PublishServices(rpcPublisher);

            var options = new RpcServerOptions
            {
                Serializer = new ProtobufRpcSerializer(),
            };
            
            var grpcServer = new GrpcServer(rpcPublisher, serviceProvider, options);
            grpcServer.AddEndPoint(CreateGrpcEndPoint(50051));

            var lightweightServer = new LightweightRpcServer(rpcPublisher, serviceProvider, options);

            var sslOptions = new SslServerOptions(new X509Certificate2(TestCertificates.ServerPFXPath, "1111"));
            lightweightServer.AddEndPoint(
                new TcpRpcEndPoint("127.0.0.1", 50052, false, sslOptions));

            Console.WriteLine("Starting gRPC server and lightweight RPC server.");

            // Once the gRPC server is started, it is no longer possible to register service definitions.
            grpcServer.Start();
            lightweightServer.Start();

            Console.WriteLine("gRPC server and lightweight RPC server are running, press any key to shutdown.");

            Console.ReadKey();

            Console.WriteLine("Shutting down servers.");

            await Task.WhenAll(grpcServer.ShutdownAsync(), lightweightServer.ShutdownAsync());

            Console.WriteLine("Ended");
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            // Needed for the MailboxManager service
            services.AddSingleton<MailQueueRepository>();

            // This is one way of configuring service options. 
            services.Configure<RpcServiceOptions<IMailBoxManagerService>>(options => options.AllowAutoPublish = true);

            // Another way is to provide RpcServiceOptions when calling services.RegisterRpcService.
            //
            // E.g. services.RegisterRpcService<IMailBoxManagerService>(new RpcServiceOptions { AllowAutoPublish = true });

            //
            // Add RPC singleton implementations
            //

            // GreeterService is published as a singleton (e.g. 
            // as grpc://localhost:50051/Greeter.GreeterService),
            // but the actual implementation instance will be created
            // per call.
            services.AddTransient<GreeterServiceImpl>();

            // The same instance of MailboxManager will be used 
            // for all remote calls.
            services.AddSingleton<MailBoxManager>();
        }


        private static GrpcServerEndPoint CreateGrpcEndPoint(int port)
        {
            var certificatePair = new GrpcCore.KeyCertificatePair(
                File.ReadAllText(Path.Combine(TestCertificates.ServerCertDir, "server.crt")),
                File.ReadAllText(Path.Combine(TestCertificates.ServerCertDir, "server.key")));
            var credentials = new GrpcCore.SslServerCredentials(new GrpcCore.KeyCertificatePair[] { certificatePair });

            return new GrpcServerEndPoint("localhost", port, false, credentials);
        }

        private static void PublishServices(RpcServicePublisher publisher)
        {
            // Publishing RPC services will automatically register the service interfaces,
            // unless the gRPC server has already been started.
            // If the server has already been started and an unregistered interface is published,
            // then an exception will be thrown.
            publisher.PublishSingleton<GreeterServiceImpl, IGreeterService>();
            publisher.PublishSingleton<MailBoxManager, IMailBoxManagerService>();
        }

        private static void RegisterServiceDefinitions(RpcServiceDefinitionsBuilder definitionsBuilder)
        {
            definitionsBuilder.RegisterService<IMailboxService>();
        }
    }
}
