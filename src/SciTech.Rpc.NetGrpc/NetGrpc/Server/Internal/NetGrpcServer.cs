#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB
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
        public NetGrpcServer(RpcServicePublisher servicePublisher, IOptions<RpcServerOptions> options, ILogger<NetGrpcServer>? logger)
            : this(servicePublisher, servicePublisher, servicePublisher.DefinitionsProvider, options.Value, logger)
        {
        }

        internal NetGrpcServer(
            IRpcServicePublisher servicePublisher,
            IRpcServiceActivator serviceImplProvider,
            IRpcServiceDefinitionsProvider serviceDefinitionsProvider,
            //ServiceMethodProviderContext<NetGrpcServiceActivator>? context,
            RpcServerOptions? options,
            ILogger<NetGrpcServer>? logger)
            : base(servicePublisher, serviceImplProvider, serviceDefinitionsProvider, options, logger)
        {
        }


        internal static Task<RpcServicesQueryResponse> QueryServices(NetGrpcServer server, RpcObjectRequest request,
            GrpcCore.ServerCallContext callContext)
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
                    throw new GrpcCore.RpcException(new GrpcCore.Status(GrpcCore.StatusCode.Unknown, rpcError.Message),
                        new GrpcCore.Metadata
                        {
                            {WellKnownHeaderKeys.ErrorInfo, serializedError }
                        });
                }
                else
                {
                    var metadata = new GrpcCore.Metadata
                    {
                        { WellKnownHeaderKeys.ErrorType, rpcError.ErrorType },
                        { WellKnownHeaderKeys.ErrorMessage, rpcError.Message },
                        { WellKnownHeaderKeys.ErrorCode, rpcError.ErrorCode }
                    };

                    if (rpcError.ErrorDetails != null)
                    {
                        metadata.Add(new GrpcCore.Metadata.Entry(WellKnownHeaderKeys.ErrorDetails, rpcError.ErrorDetails));
                    }

                    throw new GrpcCore.RpcException(new GrpcCore.Status(GrpcCore.StatusCode.Unknown, rpcError.Message), metadata);
                }
            }
        }
    }
}
