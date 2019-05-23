using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using SciTech.Rpc;
using System;
using System.Linq;

namespace NetGrpcServer
{
    public static class Program
    {
        public static IWebHostBuilder CreateHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
            .ConfigureKestrel(options =>
           {
               options.Limits.MinRequestBodyDataRate = null;
               options.ListenLocalhost(50051, listenOptions =>
                   {
                       listenOptions.UseHttps(TestCertificates.ServerPFXPath, "1111");
                       listenOptions.Protocols = HttpProtocols.Http2;
                   });
           })
            .UseStartup<Startup>();

        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }
    }
}
