using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Server
{
    public interface IRpcServer : IDisposable
    {
        ImmutableArray<RpcServerCallInterceptor> CallInterceptors { get; }

        IRpcServicePublisher ServicePublisher { get; }

        /// <summary>
        /// TODO: Should be moved to a behavior/configuration class.
        /// </summary>
        void AddCallInterceptor(RpcServerCallInterceptor callInterceptor);

        void AddEndPoint(IRpcServerEndPoint endPoint);

        Task ShutdownAsync();

        void Start();

        /// <summary>
        /// Indicates that service instances returned from an RPC call is 
        /// allowed to be automatically published.
        /// TODO: Should be moved to a behavior/configuration class.
        /// </summary>
        bool AllowAutoPublish { get; }
    }
}
