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

using SciTech.Rpc.Client;
using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace SciTech.Rpc.Lightweight.Client.Internal
{
    internal class LightweightProxyGenerator : RpcProxyGenerator<LightweightProxyBase, LightweightProxyArgs, LightweightMethodDef>
    {
        private readonly ConditionalWeakTable<IRpcSerializer, LightweightSerializersCache> serializerToMethodSerializersCache
            = new ConditionalWeakTable<IRpcSerializer, LightweightSerializersCache>();

        private readonly object syncRoot = new object();

        internal LightweightProxyGenerator(IRpcProxyDefinitionsProvider? proxyServicesProvider = null) : base(proxyServicesProvider)
        {
        }

        protected override RpcObjectProxyFactory CreateProxyFactory(
            Func<LightweightProxyArgs, LightweightMethodDef[], RpcProxyBase> proxyCreator,
            IReadOnlyCollection<string>? implementedServices,
            LightweightMethodDef[] proxyMethods)
        {
            var proxyServicesProvider = this.ProxyServicesProvider;
            return (RpcObjectId objectId, IRpcServerConnection connection, SynchronizationContext? syncContext) =>
            {
                if (connection is LightweightRpcConnection lightweightConnection)
                {
                    var args = new LightweightProxyArgs
                    (
                        objectId: objectId,
                        callInterceptors: lightweightConnection.Options.Interceptors,
                        connection: lightweightConnection,
                        serializer: lightweightConnection.Serializer,
                        methodSerializersCache: this.GetMethodSerializersCache(lightweightConnection.Serializer),
                        implementedServices: implementedServices,
                        proxyServicesProvider: proxyServicesProvider,
                        syncContext: syncContext
                    );

                    return proxyCreator(args, proxyMethods);
                }
                else
                {
                    throw new InvalidOperationException($"{nameof(LightweightProxyGenerator)} should only be used for {nameof(LightweightRpcConnection)}.");
                }
            };
        }

        private LightweightSerializersCache GetMethodSerializersCache(IRpcSerializer serializer)
        {
            lock (this.syncRoot)
            {
                if (this.serializerToMethodSerializersCache.TryGetValue(serializer, out var existingMethodsCache))
                {
                    return existingMethodsCache;
                }

                var methodsCache = new LightweightSerializersCache(serializer);
                this.serializerToMethodSerializersCache.Add(serializer, methodsCache);

                return methodsCache;
            }
        }

        internal static class Factory
        {
            private static readonly LightweightProxyGenerator DefaultGenerator = new LightweightProxyGenerator(null);

            private static readonly ConditionalWeakTable<IRpcProxyDefinitionsProvider, LightweightProxyGenerator> proxyGenerators
                 = new ConditionalWeakTable<IRpcProxyDefinitionsProvider, LightweightProxyGenerator>();

            private static readonly object syncRoot = new object();


            internal static LightweightProxyGenerator CreateProxyGenerator(IRpcProxyDefinitionsProvider? definitionsProvider)
            {
                if (definitionsProvider == null)
                {
                    return DefaultGenerator;
                }

                lock (syncRoot)
                {
                    if (!proxyGenerators.TryGetValue(definitionsProvider, out var generator))
                    {
                        generator = new LightweightProxyGenerator(definitionsProvider);
                        proxyGenerators.Add(definitionsProvider, generator);
                    }

                    return generator;
                }
            }
        }

    }
}
