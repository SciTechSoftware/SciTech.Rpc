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

namespace SciTech.Rpc.Server
{
    /// <summary>
    /// Contains options for the server side implementation of RPC services.
    /// TODO: Service options are still being designed. This class and the way options
    /// are configured will be changed in future releases.
    /// </summary>
    public class RpcServiceOptions
    {
        private List<IRpcServerExceptionConverter>? exceptionConverters;

        private List<RpcServerCallInterceptor>? interceptors;

        /// <summary>
        /// Gets or sets a value indicating whether service instances may be automatically published
        /// when returned from a service implementation method.
        /// </summary>
        public bool? AllowAutoPublish { get; set; }

        public TimeSpan? CallTimeout { get; set; }

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

        /// <summary>
        /// Gets or sets the maximum message size in bytes that can be received by the server.
        /// </summary>
        public int? ReceiveMaxMessageSize { get; set; }

        /// <summary>
        /// Gets or sets the maximum message size in bytes that can be sent from the server.
        /// </summary>
        public int? SendMaxMessageSize { get; set; }

        public IRpcSerializer? Serializer { get; set; }

        public TimeSpan? StreamingCallTimeout { get; set; }
    }

    public class RpcServiceOptions<T> : RpcServiceOptions
    {
    }
}
