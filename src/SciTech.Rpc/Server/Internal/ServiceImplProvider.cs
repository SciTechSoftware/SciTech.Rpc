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

        ActivatedService<TService>? GetActivatedService<TService>(IServiceProvider? serviceProvider, RpcObjectId id) where TService : class;

        bool CanGetActivatedService<TService>(RpcObjectId id) where TService : class;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Internal")]
    public readonly struct ActivatedService<TService> where TService : class
    {
        public ActivatedService(TService service, bool shouldDispose)
        {
            this.Service = service;
            this.ShouldDispose = shouldDispose;
        }

        public TService Service { get; }

        public bool ShouldDispose { get; }
    }
}
