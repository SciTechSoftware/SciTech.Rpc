﻿using SciTech.Rpc.Client;
using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Grpc.Client.Internal;
using System;

namespace SciTech.Rpc.Grpc.Client
{
    public class GrpcProxyProvider : RpcProxyProvider
    {
        /// <summary>
        /// Default gRPC proxy provider. Note that any proxy factories generated by this provider will
        /// not garbage collected (until process/AppDomain exit).
        /// </summary>
        public static readonly GrpcProxyProvider Default = new GrpcProxyProvider();

        public GrpcProxyProvider(IRpcProxyDefinitionsProvider? proxyServicesProvider = null)
        {
            this.ProxyGenerator = new GrpcProxyGenerator(proxyServicesProvider);
        }

        protected override IRpcProxyGenerator ProxyGenerator { get; }
    }
}
