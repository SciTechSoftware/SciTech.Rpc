using Greeter;
using Mailer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SciTech.Rpc.NetGrpc.Server;
using SciTech.Rpc.Server;
using System;
using System.Linq;

namespace NetGrpcServer
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddNetGrpc(options=>
            {
                options.AllowAutoPublish = true;
            });

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

            // The mailbox service will not be published as a singleton
            // but the service interface must still be registered.
            // Can also be registered in the Configure method using IApplicationBuilder 
            // (before calling MapNetGrpcServices).
            services.RegisterRpcService<IMailboxService>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            // RPC services can be registered and published here,
            // but registration must occur before calling MapNetGrpcServices.

            // IMailboxService has already been registered in ConfigureServices,
            // so let's not do it again.            
            // app.RegisterRpcService<IMailboxService>();

            // Publishing RPC services will automatically register the service interfaces,
            // unless MapNetGrpcServices has been called.
            // If services have already been mapped and an unregistered interface is published,
            // then an exception will be thrown.
            app.PublishRpcSingleton<GreeterServiceImpl, IGreeterService>();
            app.PublishRpcSingleton<MailBoxManager, IMailBoxManagerService>();

            app.UseEndpoints(endpoints =>
            {
                // Map all registered RPC service interfaces to make 
                // them callable as gRPC HTTP/2 operations.
                // TODO: NetGrpc configuration should be improved.
                endpoints.MapNetGrpcServices();
            });
        }
    }
}
