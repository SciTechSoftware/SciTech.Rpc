using Greeter;
using Mailer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using SciTech.Rpc.NetGrpc.Server;
using SciTech.Rpc.Server;
using SciTech.Rpc;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using Ticketer;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace NetGrpcServer
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddNetGrpc()
                .AddServiceOptions<IMailBoxManagerService>(options =>
                {
                    options.AllowAutoPublish = true;
                });

            services.AddSingleton<MailQueueRepository>();
            services.AddSingleton<TicketRepository>();
            
            services.AddHttpContextAccessor();
            services.AddAuthorization(options =>
            {
                options.AddPolicy(JwtBearerDefaults.AuthenticationScheme, policy =>
                {
                    policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
                    policy.RequireClaim(ClaimTypes.Name);
                });
            });
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters =
                        new TokenValidationParameters
                        {
                            ValidateAudience = false,
                            ValidateIssuer = false,
                            ValidateActor = false,
                            ValidateLifetime = true,
                            IssuerSigningKey = SecurityKey,
                        };
                })
                .AddCertificate(options =>
                {
                    // Not recommended in production environments. The example is using a test certificate
                    options.RevocationMode = X509RevocationMode.NoCheck;
                    options.Events.OnAuthenticationFailed = CertificateAuthenticationFailedContext => Task.CompletedTask;
                    options.Events.OnCertificateValidated = CertificateAuthenticationFailedContext => Task.CompletedTask;
                });

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

            // The same instance of TicketerImpl will be used 
            // for all remote calls.
            services.AddSingleton<TicketerImpl>();

            // The mailbox service will not be published as a singleton
            // but the service interface must still be registered.
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
            app.UseAuthentication();
            app.UseAuthorization();

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
            app.PublishRpcSingleton<TicketerImpl, ITicketerService>();

            app.UseEndpoints(endpoints =>
            {
                // Map all registered RPC service interfaces to make 
                // them callable as gRPC HTTP/2 operations.
                // After MapNetGrpcServices is called, it is no longer
                // possible to register new RPC interfaces. But publishing
                // singleton and instance implementations of already 
                // registered interfaces are allowed.
                endpoints.MapNetGrpcServices();

                endpoints.MapGet("/generateJwtToken", context =>
                {
                    return context.Response.WriteAsync(GenerateJwtToken(context.Request.Query["name"]));
                });
            });
        }

        private string GenerateJwtToken(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new InvalidOperationException("Name is not specified.");
            }

            var claims = new[] { new Claim(ClaimTypes.Name, name) };
            var credentials = new SigningCredentials(SecurityKey, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken("ExampleServer", "ExampleClients", claims, expires: DateTime.Now.AddSeconds(60), signingCredentials: credentials);
            return JwtTokenHandler.WriteToken(token);
        }

        private readonly JwtSecurityTokenHandler JwtTokenHandler = new JwtSecurityTokenHandler();
        private readonly SymmetricSecurityKey SecurityKey = new SymmetricSecurityKey(Guid.NewGuid().ToByteArray());
    }
}
