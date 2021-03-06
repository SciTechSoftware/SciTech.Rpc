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

using Microsoft.Extensions.Logging;
using SciTech.Collections.Immutable;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Serialization;
using System;
using System.Collections.Immutable;

namespace SciTech.Rpc.Server.Internal
{
    /// <summary>
    /// Extends the <see cref="IRpcServer"/> with a property to retrieve 
    /// the <see cref="IRpcServiceActivator"/> associated with <see cref="IRpcServer.ServicePublisher"/>.
    /// </summary>
    public interface IRpcServerCore : IRpcServer
    {
        ImmutableArrayList<RpcServerCallInterceptor> CallInterceptors { get; }

        /// <summary>
        /// Gets the custom <see cref="RpcServerFaultHandler"/> that has been initialized 
        /// using the <see cref="ExceptionConverters"/>. If there are no <c>ExceptionConverters</c>
        /// this property will return <c>null</c>.
        /// </summary>
        /// <value>An <see cref="RpcServerFaultHandler"/> that has been initialized 
        /// using the <see cref="ExceptionConverters"/> , or <c>null</c> if there are no exception converters.</value>
        RpcServerFaultHandler? CustomFaultHandler { get; }

        /// <summary>
        /// Gets the exceptions converters associated with this server. If the array is 
        /// not empty, the <see cref="CustomFaultHandler"/> will represent the fault handler
        /// for the combined exception converters.
        /// </summary>
        ImmutableArrayList<IRpcServerExceptionConverter> ExceptionConverters { get; }

        bool HasContextAccessor { get; }
        
        ILoggerFactory? LoggerFactory { get; }

        IRpcSerializer Serializer { get; }

        /// <summary>
        /// Gets the <see cref="IRpcServiceDefinitionsProvider"/> associated with <see cref="IRpcServer.ServicePublisher"/>.
        /// </summary>
        IRpcServiceDefinitionsProvider ServiceDefinitionsProvider { get; }

        /// <summary>
        /// Gets the <see cref="IRpcServiceActivator"/> associated with <see cref="IRpcServer.ServicePublisher"/>.
        /// </summary>
        IRpcServiceActivator ServiceActivator { get; }

        IServiceProvider? ServiceProvider { get; }

        /// <summary>
        /// Can be used by the server implementation to convert the exception to a suitable type
        /// to be "returned" to client.
        /// </summary>
        /// <param name="exception"></param>
        /// <param name="serializer"></param>
        void HandleCallException(Exception exception, IRpcSerializer? serializer);
    }
}
