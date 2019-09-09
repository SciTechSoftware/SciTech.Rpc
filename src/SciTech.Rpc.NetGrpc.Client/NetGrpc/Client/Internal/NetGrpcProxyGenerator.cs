#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using SciTech.Rpc.Client;
using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.NetGrpc.Client;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.Grpc.Client.Internal
{
    public class NetGrpcProxyArgs : RpcProxyArgs
    {
        internal NetGrpcProxyArgs(IRpcServerConnection connection,
                               GrpcCore.CallInvoker callInvoker,
                               RpcObjectId objectId,
                               GrpcMethodsCache methodsCache,
                               IRpcSerializer serializer,
                               IReadOnlyCollection<string>? implementedServices,
                               IRpcProxyDefinitionsProvider proxyServicesProvider,
                               SynchronizationContext? syncContext) 
            : base(connection, objectId, serializer, implementedServices, proxyServicesProvider, syncContext)
        {
            this.CallInvoker = callInvoker;
            this.MethodsCache = methodsCache;
        }

        internal GrpcCore.CallInvoker CallInvoker { get; }

        internal GrpcMethodsCache MethodsCache { get; }
    }

    public class NetGrpcProxyGenerator : RpcProxyGenerator<NetGrpcProxyBase, NetGrpcProxyArgs, NetGrpcProxyMethod>
    {
        private readonly ConditionalWeakTable<IRpcSerializer, GrpcMethodsCache> serializerToMethodsCache = new ConditionalWeakTable<IRpcSerializer, GrpcMethodsCache>();

        private readonly object syncRoot = new object();

        public NetGrpcProxyGenerator(IRpcProxyDefinitionsProvider? proxyServicesProvider) 
            : base( proxyServicesProvider )
        {
        }

        public NetGrpcProxyGenerator() : base(null)
        {
        }

        protected override RpcObjectProxyFactory CreateProxyFactory(
            Func<NetGrpcProxyArgs, NetGrpcProxyMethod[], RpcProxyBase> proxyCreator,
            IReadOnlyCollection<string>? implementedServices,
            NetGrpcProxyMethod[] proxyMethods)
        {
            var proxyServicesProvider = this.ProxyServicesProvider;
            return (RpcObjectId objectId, IRpcServerConnection connection, SynchronizationContext? syncContext) =>
            {
                if (connection is NetGrpcServerConnection grpcConnection)
                {
                    var callInvoker = grpcConnection.CallInvoker;
                    if (callInvoker == null)
                    {
                        throw new InvalidOperationException("Connection has been closed.");
                    }

                    var methodsCache = this.GetMethodCache(grpcConnection.Serializer);

                    var args = new NetGrpcProxyArgs
                    (
                        objectId: objectId,
                        connection: grpcConnection,
                        callInvoker: callInvoker,
                        methodsCache: methodsCache,
                        serializer: grpcConnection.Serializer,
                        implementedServices: implementedServices,
                        proxyServicesProvider: proxyServicesProvider,
                        syncContext: syncContext
                    );

                    
                    return proxyCreator(args, proxyMethods);
                }
                else
                {
                    throw new InvalidOperationException("GrpcProxyGenerator should only be used for GrpcServerConnection.");
                }
            };
        }

        private GrpcMethodsCache GetMethodCache(IRpcSerializer serializer)
        {
            lock (this.syncRoot)
            {
                if (this.serializerToMethodsCache.TryGetValue(serializer, out var existingMethodsCache))
                {
                    return existingMethodsCache;
                }

                var methodsCache = new GrpcMethodsCache(serializer);
                this.serializerToMethodsCache.Add(serializer, methodsCache);

                return methodsCache;
            }
        }
    }
}
