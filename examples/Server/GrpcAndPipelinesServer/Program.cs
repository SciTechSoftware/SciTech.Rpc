using Greeter;
using Mailer;
using Microsoft.Extensions.DependencyInjection;
using SciTech.Rpc;
using SciTech.Rpc.Grpc.Server;
using SciTech.Rpc.Pipelines.Server;
using SciTech.Rpc.Server;
using System;
using System.IO;
using System.Threading.Tasks;
using GrpcCore = Grpc.Core;

namespace GrpcAndPipelinesServer
{
    internal class Program
    {
        public static async Task Main(string[] args)
        {
            // This example shows how to explicitly setup a gRPC RPC server and a Pipelines RPC server
            // with a common service publisher.
            // 
            // In a real scenario, it is probably more suitable to use the .NET generic host
            // (https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/generic-host).
            //
            // The NetGrpcServer example shows what a host setup could look like.


            Console.WriteLine("Initializing gRPC server and pipelines server.");
            var serviceCollection = new ServiceCollection();

            ConfigureServices(serviceCollection);

            var serviceProvider = serviceCollection.BuildServiceProvider();

            var definitionsBuilder = new RpcServiceDefinitionBuilder();
            var rpcPublisher = new RpcServicePublisher(definitionsBuilder);

            RegisterServiceDefinitions(definitionsBuilder);
            PublishServices(rpcPublisher);

            var options = new RpcServiceOptions
            {
                Serializer = new ProtobufSerializer()
            };
            
            var grpcServer = new GrpcServer(rpcPublisher, serviceProvider, options);
            grpcServer.AllowAutoPublish = true;
            grpcServer.AddEndPoint(CreateGrpcEndPoint(50051));

            var pipelinesServer = new RpcPipelinesServer(rpcPublisher, serviceProvider, options);
            pipelinesServer.AllowAutoPublish = true;
            pipelinesServer.AddEndPoint(new TcpPipelinesEndPoint("127.0.0.1", 50052, false));

            Console.WriteLine("Starting gRPC server and pipelines server.");

            // Once the gRPC server is started, it is no longer possible to register service definitions.
            grpcServer.Start();
            pipelinesServer.Start();

            Console.WriteLine("gRPC server and pipelines server are running, press any key to shutdown.");

            Console.ReadKey();

            Console.WriteLine("Shutting down servers.");

            await Task.WhenAll(grpcServer.ShutdownAsync(), pipelinesServer.ShutdownAsync());

            Console.WriteLine("Ended");
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            // Needed for the MailboxManager service
            services.AddSingleton<MailQueueRepository>();

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

        private static void RegisterServiceDefinitions(RpcServiceDefinitionBuilder definitionsBuilder)
        {
            definitionsBuilder.RegisterService<IMailboxService>();
        }
    }
}
