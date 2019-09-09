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
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using GrpcCore = Grpc.Core;

#if FEATURE_NET_GRPC
namespace SciTech.Rpc.NetGrpc.Client.Internal
#else
namespace SciTech.Rpc.Grpc.Client.Internal
#endif
{
    public class GrpcProxyArgs : RpcProxyArgs
    {
        internal GrpcProxyArgs(IRpcServerConnection connection,
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

    internal class GrpcProxyGenerator : RpcProxyGenerator<GrpcProxyBase, GrpcProxyArgs, GrpcProxyMethod>
    {
        private readonly ConditionalWeakTable<IRpcSerializer, GrpcMethodsCache> serializerToMethodsCache = new ConditionalWeakTable<IRpcSerializer, GrpcMethodsCache>();

        private readonly object syncRoot = new object();

        public GrpcProxyGenerator(IRpcProxyDefinitionsProvider? proxyServicesProvider) 
            : base( proxyServicesProvider )
        {
        }

        public GrpcProxyGenerator() : base(null)
        {
        }

        protected override RpcObjectProxyFactory CreateProxyFactory(
            Func<GrpcProxyArgs, GrpcProxyMethod[], RpcProxyBase> proxyCreator,
            IReadOnlyCollection<string>? implementedServices,
            GrpcProxyMethod[] proxyMethods)
        {
            var proxyServicesProvider = this.ProxyServicesProvider;
            return (RpcObjectId objectId, IRpcServerConnection connection, SynchronizationContext? syncContext) =>
            {
                if (connection is IGrpcServerConnection grpcConnection)
                {
                    var callInvoker = grpcConnection.CallInvoker;
                    if (callInvoker == null)
                    {
                        throw new InvalidOperationException("Connection has been closed.");
                    }

                    var methodsCache = this.GetMethodCache(grpcConnection.Serializer);

                    var args = new GrpcProxyArgs
                    (
                        objectId: objectId,
                        connection: connection,
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
