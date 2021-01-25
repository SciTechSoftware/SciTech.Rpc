#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using SciTech.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace SciTech.Rpc.Server.Internal
{
    public interface IRpcServiceActivator
    {
        ImmutableArray<string> GetPublishedServices(RpcObjectId objectId);

        IImmutableList<Type> GetPublishedSingletons();

        IOwned<TService>? GetActivatedService<TService>(IServiceProvider? serviceProvider, RpcObjectId id) where TService : class;

        bool CanGetActivatedService<TService>(RpcObjectId id) where TService : class;
    }
}
