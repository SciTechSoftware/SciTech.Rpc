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

using System;
using System.Collections.Generic;
using System.Threading;

namespace SciTech.Rpc.Client.Internal
{
    public delegate RpcProxyBase RpcObjectProxyFactory(RpcObjectId objectId, IRpcChannel connection, SynchronizationContext? synchronizationContext);

    public delegate RpcProxyBase RpcSingletonProxyFactory(RpcObjectId objectId, IRpcChannel connection, SynchronizationContext? synchronizationContext);

    public interface IRpcProxyGenerator
    {
        RpcObjectProxyFactory GenerateObjectProxyFactory<TService>(IReadOnlyCollection<string>? implementedServices) where TService : class;
    }
}
