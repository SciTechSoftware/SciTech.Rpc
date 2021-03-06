﻿using Microsoft.Extensions.Options;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server.Internal
{
    internal class ServiceDiscovery
    {
        private readonly IRpcSerializer discoverySerializer;
        private readonly LightweightRpcServer server;

        public ServiceDiscovery(LightweightRpcServer server, IRpcSerializer discoverySerializer)
        {
            this.server = server;
            this.discoverySerializer = discoverySerializer;
        }

        internal void AddDiscoveryMethods(ILightweightMethodBinder binder)
        {
            var getPublishedSingletonsStub = new LightweightMethodStub<RpcDiscoveryRequest, RpcPublishedSingletonsResponse>(
                ServiceDiscoveryOperations.GetPublishedSingletons,
                (request, serviceProvider, context) => this.GetPublishedSingletonsAsync(request, serviceProvider, context),
                this.discoverySerializer, null, false);
            binder.AddMethod(getPublishedSingletonsStub);

            var getConnectionInfoStub = new LightweightMethodStub<RpcDiscoveryRequest, RpcConnectionInfoResponse>(
                ServiceDiscoveryOperations.GetConnectionInfo,
                (request, serviceProvider, context) => this.GetConnectionInfoAsync(request, context),
                this.discoverySerializer, null, false);
            binder.AddMethod(getConnectionInfoStub);
        }

        private ValueTask<RpcConnectionInfoResponse> GetConnectionInfoAsync(RpcDiscoveryRequest request, LightweightCallContext context)
        {
            var connectionInfo = context.EndPoint.GetConnectionInfo(this.server.ServicePublisher.ServerId);
            return new ValueTask<RpcConnectionInfoResponse>(new RpcConnectionInfoResponse { ClientId = request.ClientId, ConnectionInfo = connectionInfo });
        }

        private ValueTask<RpcPublishedSingletonsResponse> GetPublishedSingletonsAsync(RpcDiscoveryRequest request, IServiceProvider? serviceProvider, LightweightCallContext context)
        {
            var connectionInfo = context.EndPoint.GetConnectionInfo(this.server.ServicePublisher.ServerId);
            var publishedSingletons = this.GetPublishedSingletonsList(serviceProvider);
            return new ValueTask<RpcPublishedSingletonsResponse>(new RpcPublishedSingletonsResponse
            {
                ClientId = request.ClientId,
                ConnectionInfo = connectionInfo,
                Services = publishedSingletons.ToArray()
            });
        }

        private IReadOnlyList<RpcPublishedSingleton> GetPublishedSingletonsList(IServiceProvider? serviceProvider)
        {
            // TODO: This method is not particularily fast. Maybe add some caching? Or retrieve information from service stubs.
            var publishedSingletons = new List<RpcPublishedSingleton>();
            foreach (var type in this.server.ServiceActivator.GetPublishedSingletons())
            {
                var serviceInfo = RpcBuilderUtil.TryGetServiceInfoFromType(type);
                if (serviceInfo != null)
                {
                    IOptions<RpcServerOptions>? options = null;

                    if (serviceProvider != null)
                    {
                        options = (IOptions<RpcServerOptions>?)serviceProvider.GetService(
                            typeof(IOptions<>).MakeGenericType(
                                typeof(RpcServiceOptions<>).MakeGenericType(type)));
                    }

                    var registeredOptions = this.server.ServiceDefinitionsProvider.GetServiceOptions(type);
                    if (options?.Value?.AllowDiscovery ?? registeredOptions?.AllowDiscovery ?? true)
                    {
                        publishedSingletons.Add(new RpcPublishedSingleton
                        {
                            Name = serviceInfo.FullName
                        });
                    }
                }
            }

            return publishedSingletons;
        }
    }
}
