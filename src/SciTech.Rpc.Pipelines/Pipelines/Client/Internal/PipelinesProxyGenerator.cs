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

namespace SciTech.Rpc.Pipelines.Client.Internal
{
    public class PipelinesProxyGenerator : RpcProxyGenerator<PipelinesProxyBase, PipelinesProxyArgs, PipelinesMethodDef>
    {
        public PipelinesProxyGenerator(IRpcProxyDefinitionsProvider? proxyServicesProvider = null) : base(proxyServicesProvider)
        {
        }

        protected override RpcObjectProxyFactory CreateProxyFactory(
            Func<PipelinesProxyArgs, PipelinesMethodDef[], RpcProxyBase> proxyCreator,
            IReadOnlyCollection<string>? implementedServices,
            PipelinesMethodDef[] proxyMethods)
        {
            var proxyServicesProvider = this.ProxyServicesProvider;
            return (RpcObjectId objectId, IRpcServerConnection connection, SynchronizationContext? syncContext) =>
            {
                if (connection is PipelinesServerConnection pipelinesConnection)
                {
                    var args = new PipelinesProxyArgs
                    (
                        objectId: objectId,
                        callInterceptors: pipelinesConnection.CallInterceptors,
                        connection: pipelinesConnection,
                        serializer: pipelinesConnection.Serializer,
                        implementedServices: implementedServices,
                        proxyServicesProvider: proxyServicesProvider,
                        syncContext: syncContext
                    );

                    return proxyCreator(args, proxyMethods);
                }
                else
                {
                    throw new InvalidOperationException($"{nameof(PipelinesProxyGenerator)} should only be used for {nameof(PipelinesServerConnection)}.");
                }
            };
        }
    }
}
