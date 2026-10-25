using Greeter;
using Mailer;
using Microsoft.Extensions.DependencyInjection;
using SciTech.Rpc;
using SciTech.Rpc.Lightweight.Server;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server;
using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace LightweightServer
{
    internal class Program
    {
        public static async Task Main(string[] args)
        {
            // This example shows how to explicitly setup a lightweight RPC server with a service publisher.
            // The NetGrpcServer example publishes the same services using gRPC.
            // 
            // In a real scenario, it is probably more suitable to use the .NET generic host
            // (https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/generic-host).
            //
            // The NetGrpcServer example shows what a host setup could look like.


            Console.WriteLine("Initializing lightweight RPC server.");
            var serviceCollection = new ServiceCollection();

            ConfigureServices(serviceCollection);

            var serviceProvider = serviceCollection.BuildServiceProvider();

            var definitionsBuilder = new RpcServiceDefinitionsBuilder();
            var rpcPublisher = new RpcServicePublisher(definitionsBuilder);

            RegisterServiceDefinitions(definitionsBuilder);
            PublishServices(rpcPublisher);

            var lightweightServer = new LightweightRpcServer(rpcPublisher, serviceProvider);

            var sslOptions = new SslServerOptions(TestCertificates.LoadServerCertificate());
            lightweightServer.AddEndPoint(
                new TcpRpcEndPoint("127.0.0.1", 50052, false, sslOptions ));

            Console.WriteLine("Starting lightweight RPC server.");

            // Once the server is started, it is no longer possible to register service definitions.
            lightweightServer.Start();

            Console.WriteLine("Lightweight RPC server is running, press any key to shutdown.");

            Console.ReadKey();

            Console.WriteLine("Shutting down server.");

            await lightweightServer.ShutdownAsync();

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
            // as lightweight.tcp://localhost:50052/Greeter.GreeterService),
            // but the actual implementation instance will be created
            // per call.
            services.AddTransient<GreeterServiceImpl>();

            // The same instance of MailboxManager will be used 
            // for all remote calls.
            services.AddSingleton<MailBoxManager>();
        }


        private static void PublishServices(RpcServicePublisher publisher)
        {
            // Publishing RPC services will automatically register the service interfaces,
            // unless the server has already been started.
            // If the server has already been started and an unregistered interface is published,
            // then an exception will be thrown.
            publisher.PublishSingleton<IGreeterService, GreeterServiceImpl>();
            publisher.PublishSingleton<IMailBoxManagerService, MailBoxManager>();
        }

        private static void RegisterServiceDefinitions(RpcServiceDefinitionsBuilder definitionsBuilder)
        {
            definitionsBuilder.RegisterService<IMailboxService>();
        }
    }
}
