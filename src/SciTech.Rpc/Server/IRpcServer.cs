using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace SciTech.Rpc.Server
{
    public interface IRpcServer : IDisposable
    {
        /// <summary>
        /// Indicates that service instances returned from an RPC call is 
        /// allowed to be automatically published.
        /// </summary>
        bool AllowAutoPublish { get; }

        ImmutableArray<RpcServerCallInterceptor> CallInterceptors { get; }

        IRpcServicePublisher ServicePublisher { get; }

        void AddCallInterceptor(RpcServerCallInterceptor callInterceptor);

        void AddEndPoint(IRpcServerEndPoint endPoint);

        Task ShutdownAsync();

        void Start();
    }

    /// <summary>
    /// Contains options for the server side implementation of RPC services.
    /// TODO: Service options are still being designed. This class and the way options
    /// are configured will be changed in future releases.
    /// </summary>
    public class RpcServiceOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether service instances may be automatically published
        /// when returned from a service implementation method.
        /// </summary>
        public bool AllowAutoPublish { get; set; }

        public TimeSpan? CallTimeout { get; set; }

        public IReadOnlyList<RpcServerCallInterceptor>? Interceptors { get; set; }

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
}
