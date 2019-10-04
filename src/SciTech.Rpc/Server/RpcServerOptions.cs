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

using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;

namespace SciTech.Rpc.Server
{
    /// <summary>
    /// Contains options for the server side implementation of RPC services.
    /// </summary>
    public class RpcServerOptions
    {
        private List<IRpcServerExceptionConverter>? exceptionConverters;

        private List<RpcServerCallInterceptor>? interceptors;

        /// <summary>
        /// Gets or sets a value indicating whether service instances may be automatically published
        /// when returned from a service implementation method.
        /// </summary>
        public bool? AllowAutoPublish { get; set; }

        public List<IRpcServerExceptionConverter> ExceptionConverters
        {
            get
            {
                if (this.exceptionConverters == null)
                {
                    this.exceptionConverters = new List<IRpcServerExceptionConverter>();
                }

                return this.exceptionConverters;
            }
        }

        public List<RpcServerCallInterceptor> Interceptors
        {
            get
            {
                if (this.interceptors == null)
                {
                    this.interceptors = new List<RpcServerCallInterceptor>();
                }

                return this.interceptors;
            }
        }

        public bool IsEmpty
        {
            get => (this.exceptionConverters == null || this.exceptionConverters.Count == 0)
                && (this.interceptors == null || this.interceptors.Count == 0)
                && this.AllowAutoPublish == null
                && this.Serializer == null
                && this.ReceiveMaxMessageSize == null
                && this.SendMaxMessageSize == null
                && this.Serializer != null;
        }

        /// <summary>
        /// Gets or sets the maximum message size in bytes that can be received by the server.
        /// </summary>
        public int? ReceiveMaxMessageSize { get; set; }

        /// <summary>
        /// Gets or sets the maximum message size in bytes that can be sent from the server.
        /// </summary>
        public int? SendMaxMessageSize { get; set; }

        public IRpcSerializer? Serializer { get; set; }
    }

    /// <summary>
    /// Specialization of <see cref="RpcServerOptions"/> than can be used to configure
    /// service specific server options.
    /// </summary>
    /// <typeparam name="T">Type of the service interface.</typeparam>
    public class RpcServiceOptions<T> : RpcServerOptions
    {
    }
}
