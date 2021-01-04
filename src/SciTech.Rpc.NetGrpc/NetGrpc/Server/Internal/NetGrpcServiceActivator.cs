#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Model;
using Microsoft.Extensions.Options;
using SciTech.Rpc.Server;
using System;

namespace SciTech.Rpc.NetGrpc.Server.Internal
{
    /// <summary>
    /// Helper class that forwards the ServiceProvider for the ASP.NET Core handler to the 
    /// NetGrpc implementation.
    /// </summary>
#pragma warning disable CA1812 // Internal class is apparently never instantiated.
    internal class NetGrpcServiceActivator<TService> where TService : class
#pragma warning restore CA1812 // Internal class is apparently never instantiated.
    {
        internal readonly IServiceProvider ServiceProvider;

        public NetGrpcServiceActivator(IServiceProvider serviceProvider)
        {
            this.ServiceProvider = serviceProvider;
        }
    }

    /// <summary>
    /// An <see cref="IConfigureOptions{TOptions}"/> implementation that is used to forward suitable RpcServiceOptions options to the
    /// GrpcServiceOptions associated with <see cref="NetGrpcServiceActivator{TService}"/>.
    /// </summary>
#pragma warning disable CA1812 // Internal class is apparently never instantiated.
    internal class NetGrpcServiceActivatorConfig<TService> : IConfigureOptions<GrpcServiceOptions<NetGrpcServiceActivator<TService>>> where TService : class
#pragma warning restore CA1812 // Internal class is apparently never instantiated.
    {
        private RpcServerOptions rpcOptions;

        public NetGrpcServiceActivatorConfig(IOptions<RpcServiceOptions<TService>> options)
        {
            this.rpcOptions = options.Value;
        }

        public void Configure(GrpcServiceOptions<NetGrpcServiceActivator<TService>> options)
        {
            options.MaxReceiveMessageSize = this.rpcOptions.ReceiveMaxMessageSize ?? options.MaxReceiveMessageSize;
            options.MaxSendMessageSize = this.rpcOptions.SendMaxMessageSize ?? options.MaxSendMessageSize;
        }
    }
}
