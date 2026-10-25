#region Copyright notice and license
// Copyright (c) SciTech Software AB and contributors.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion


using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using System;
using System.Threading.Tasks;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.NetGrpc.Server.Internal
{
    /// <summary>
    /// The ASP.NET Core gRPC implementation of <see cref="RpcServerBase"/>. Will not be directly used by client code, instead it is 
    /// registered using <see cref="NetGrpcEndpointRouteBuilderExtensions.MapNetGrpcServices"/>.
    /// </summary>
    internal sealed class NetGrpcServer : RpcServerBase
    {
        private readonly IServiceProvider? serviceProvider;

        public NetGrpcServer(RpcServicePublisher servicePublisher, IServiceProvider serviceProvider, IOptions<RpcServerOptions> options, ILoggerFactory? loggerFactory)
            : this(servicePublisher, servicePublisher, servicePublisher.DefinitionsProvider, serviceProvider, options.Value, loggerFactory)
        {
        }

        internal NetGrpcServer(
            IRpcServicePublisher servicePublisher,
            IRpcServiceActivator serviceImplProvider,
            IRpcServiceDefinitionsProvider serviceDefinitionsProvider,
            IServiceProvider? serviceProvider,
            RpcServerOptions? options,
            ILoggerFactory? loggerFactory)
            : base(servicePublisher, serviceImplProvider, serviceDefinitionsProvider, options, loggerFactory)
        {
            this.serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Gets the application service provider, e.g. used to check whether an <see cref="IRpcContextAccessor"/> has been
        /// registered (see <see cref="Rpc.Server.ServiceCollectionExtensions.AddRpcContextAccessor"/>).
        /// </summary>
        protected override IServiceProvider? ServiceProvider => this.serviceProvider;


        internal static Task<RpcServicesQueryResponse> QueryServices(NetGrpcServer server, RpcObjectRequest request, GrpcCore.ServerCallContext _)
        {
            return Task.FromResult(server.QueryServices(request.Id));
        }

        protected override IRpcSerializer CreateDefaultSerializer()
        {
            return new ProtobufRpcSerializer();
        }

        protected override void HandleCallException(Exception exception, IRpcSerializer? serializer )
        {
            var rpcError = RpcError.TryCreate(exception, serializer);
            if (rpcError != null)
            {
                if (serializer != null)
                {
                    var serializedError = serializer.Serialize(rpcError);
                    throw new GrpcCore.RpcException(new GrpcCore.Status(GrpcCore.StatusCode.Unknown, rpcError.Message ?? ""),
                        new GrpcCore.Metadata
                        {
                            {WellKnownHeaderKeys.ErrorInfo, serializedError }
                        });
                }
                else
                {
                    var metadata = new GrpcCore.Metadata
                    {
                        { WellKnownHeaderKeys.ErrorType, rpcError.ErrorType ?? ""},
                        { WellKnownHeaderKeys.ErrorMessage, rpcError.Message ?? "" },
                        { WellKnownHeaderKeys.ErrorCode, rpcError.ErrorCode ?? "" }
                    };

                    if (rpcError.ErrorDetails != null)
                    {
                        metadata.Add(new GrpcCore.Metadata.Entry(WellKnownHeaderKeys.ErrorDetails, rpcError.ErrorDetails));
                    }

                    throw new GrpcCore.RpcException(new GrpcCore.Status(GrpcCore.StatusCode.Unknown, rpcError.Message ?? ""), metadata);
                }
            }
        }
    }
}
