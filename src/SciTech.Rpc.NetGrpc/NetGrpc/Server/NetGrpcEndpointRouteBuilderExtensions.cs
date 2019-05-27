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

using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SciTech.Rpc.NetGrpc.Server.Internal;
using SciTech.Rpc.Server;
using System;
using System.Collections.Generic;

namespace SciTech.Rpc.NetGrpc.Server
{
    public static class NetGrpcEndpointRouteBuilderExtensions
    {
        public static IEndpointConventionBuilder MapNetGrpcServices(
            this IEndpointRouteBuilder builder,
            RpcServiceOptions? serviceOptions = null,
            IRpcSerializer? serializer = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var serviceDefinitionsProvider = builder.ServiceProvider.GetRequiredService<IRpcServiceDefinitionsProvider>();
            var publisher = builder.ServiceProvider.GetRequiredService<RpcServicePublisher>();

            return builder.MapGrpcService<NetGrpcServiceActivator>(options =>
                {
                    options.BindAction = (serviceBinder, serviceImplProvider) =>
                    {
                        var rpcServer = new NetGrpcServer(publisher, serviceDefinitionsProvider, serviceBinder, serviceOptions, serializer);
                        rpcServer.Start();
                    };
                    options.ModelFactory = new GrpcMethodModelFactory();
                });
        }

        private class GrpcMethodModelFactory : IGrpcMethodModelFactory<NetGrpcServiceActivator>
        {
            public GrpcEndpointModel<ClientStreamingServerMethod<NetGrpcServiceActivator, TRequest, TResponse>> CreateClientStreamingModel<TRequest, TResponse>(Method<TRequest, TResponse> method)
            {
                throw new NotImplementedException();
            }

            public GrpcEndpointModel<DuplexStreamingServerMethod<NetGrpcServiceActivator, TRequest, TResponse>> CreateDuplexStreamingModel<TRequest, TResponse>(Method<TRequest, TResponse> method)
            {
                throw new NotImplementedException();
            }

            public GrpcEndpointModel<ServerStreamingServerMethod<NetGrpcServiceActivator, TRequest, TResponse>> CreateServerStreamingModel<TRequest, TResponse>(Method<TRequest, TResponse> method)
            {
                if (method is ServerStreamingMethodStub<TRequest, TResponse> streamingMethod)
                {
                    return new GrpcEndpointModel<ServerStreamingServerMethod<NetGrpcServiceActivator, TRequest, TResponse>>(streamingMethod.Invoker, new List<object>());
                }

                throw new NotSupportedException($"{nameof(GrpcMethodModelFactory)} should only be used for the SciTech.Rpc implementation of Grpc.AspNetCore.");
            }

            public GrpcEndpointModel<UnaryServerMethod<NetGrpcServiceActivator, TRequest, TResponse>> CreateUnaryModel<TRequest, TResponse>(Method<TRequest, TResponse> method)
            {
                if (method is UnaryMethodStub<TRequest, TResponse> unaryMethod)
                {
                    return new GrpcEndpointModel<UnaryServerMethod<NetGrpcServiceActivator, TRequest, TResponse>>(unaryMethod.Invoker, new List<object>());
                }

                throw new NotSupportedException($"{nameof(GrpcMethodModelFactory)} should only be used for the SciTech.Rpc implementation of Grpc.AspNetCore.");
            }
        }
    }
}
