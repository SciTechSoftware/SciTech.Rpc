﻿#region Copyright notice and license
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
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Server.Internal
{
    public interface IRpcServiceActivator
    {
        ImmutableArray<string> GetPublishedServices(RpcObjectId objectId);

        IImmutableList<Type> GetPublishedSingletons();

        ActivatedService<TService> GetActivatedService<TService>(IServiceProvider? serviceProvider, RpcObjectId id) where TService : class;

        bool CanGetActivatedService<TService>(RpcObjectId id) where TService : class;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types")]
    public struct ActivatedService<T> : IAsyncDisposable where T : class
    {
        private IAsyncDisposable? disposable;

        internal ActivatedService(T value, IAsyncDisposable? disposable)
        {
            this.Value = value;
            this.disposable = disposable;
        }

        public T Value { get; }

        public bool CanDispose => this.disposable != null;

        public ValueTask DisposeAsync()
        {
            return this.disposable?.DisposeAsync() ?? default;
        }
    }
}
