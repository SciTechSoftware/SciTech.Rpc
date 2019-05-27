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
using Microsoft.Extensions.Options;
using SciTech.Rpc.Server;
using System;

namespace SciTech.Rpc.NetGrpc.Server.Internal
{
    /// <summary>
    /// Helper class that forwards the ServiceProvider for the ASP.NET Core handler to the 
    /// NetGrpc implementation.
    /// </summary>
    internal class NetGrpcServiceActivator
    {
        internal readonly IServiceProvider ServiceProvider;

        internal NetGrpcServiceActivator(IServiceProvider serviceProvider)
        {
            this.ServiceProvider = serviceProvider;
        }
    }

    /// <summary>
    /// An <see cref="IConfigureOptions{TOptions}"/> implementation that is used to forward suitable RpcServiceOptions options to the
    /// GrpcServiceOptions associated with <see cref="NetGrpcServiceActivator"/>.
    /// </summary>
    internal class NetGrpcServiceActivatorConfig : IConfigureOptions<GrpcServiceOptions<NetGrpcServiceActivator>>
    {
        private RpcServiceOptions rpcOptions;

        public NetGrpcServiceActivatorConfig(IOptions<RpcServiceOptions> options)
        {

            this.rpcOptions = options.Value;
        }

        public void Configure(GrpcServiceOptions<NetGrpcServiceActivator> options)
        {
            options.ReceiveMaxMessageSize = this.rpcOptions.ReceiveMaxMessageSize;
            options.SendMaxMessageSize = this.rpcOptions.SendMaxMessageSize;
        }
    }
}
