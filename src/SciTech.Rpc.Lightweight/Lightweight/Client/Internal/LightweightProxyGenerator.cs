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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SciTech.Rpc.Lightweight.Client.Internal
{
    public class LightweightProxyGenerator : RpcProxyGenerator<LightweightProxyBase, LightweightProxyArgs, LightweightMethodDef>
    {
        public LightweightProxyGenerator(IRpcProxyDefinitionsProvider? proxyServicesProvider = null) : base(proxyServicesProvider)
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
                        callInterceptors: lightweightConnection.CallInterceptors,
                        connection: lightweightConnection,
                        serializer: lightweightConnection.Serializer,
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
    }
}
