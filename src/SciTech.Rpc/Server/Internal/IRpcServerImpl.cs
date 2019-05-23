using System;
using System.Collections.Generic;
using System.Text;

namespace SciTech.Rpc.Server.Internal
{
    /// <summary>
    /// Extends the <see cref="IRpcServer"/> with a property to retrieve 
    /// the <see cref="IRpcServiceActivator"/> associated with <see cref="IRpcServer.ServicePublisher"/>.
    /// </summary>
    public interface IRpcServerImpl : IRpcServer
    {
        /// <summary>
        /// Gets the <see cref="IRpcServiceActivator"/> associated with <see cref="IRpcServer.ServicePublisher"/>.
        /// </summary>
        IRpcServiceActivator ServiceImplProvider { get; }

        /// <summary>
        /// Gets the <see cref="IRpcServiceDefinitionsProvider"/> associated with <see cref="IRpcServer.ServicePublisher"/>.
        /// </summary>
        IRpcServiceDefinitionsProvider ServiceDefinitionsProvider { get; }

        IServiceProvider? ServiceProvider { get; }
    }
}
