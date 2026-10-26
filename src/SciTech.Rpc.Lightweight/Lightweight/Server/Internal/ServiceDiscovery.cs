using Microsoft.Extensions.Options;
using SciTech.Collections.Immutable;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server.Internal
{
    /// <summary>
    /// Implements the server side of the discovery operations, see the Lightweight protocol specification
    /// (docs/lightweight-protocol.md, section 7).
    /// </summary>
    internal class ServiceDiscovery
    {
        /// <summary>
        /// Room left in a discovery datagram for the frame header of a response. The header of a discovery response is
        /// 16 bytes plus the operation name and three single byte varints.
        /// </summary>
        private const int FrameHeaderAllowance = 64;

        private readonly LightweightRpcServer server;

        private readonly object syncRoot = new object();

        private ImmutableArrayList<Type>? cachedSingletonTypes;

        private DiscoverableServices? cachedServices;

        public ServiceDiscovery(LightweightRpcServer server)
        {
            this.server = server;
        }

        /// <summary>
        /// Handles a discovery request received in a datagram.
        /// </summary>
        /// <returns>The response datagram, or <c>null</c> if the request should not be answered, e.g. because the
        /// operation is unknown or the request is for another server.</returns>
        internal byte[]? HandleDatagram(in LightweightRpcFrame requestFrame, LightweightRpcEndPoint endPoint, IServiceProvider? serviceProvider)
        {
            var serializer = ServiceDiscoveryOperations.DiscoverySerializer;
            switch (requestFrame.RpcOperation)
            {
                case ServiceDiscoveryOperations.FindServers:
                    if (serializer.Deserialize<RpcFindServersRequest>(requestFrame.Payload) is RpcFindServersRequest findRequest)
                    {
                        return WriteResponse(requestFrame, this.FindServers(findRequest, serviceProvider, endPoint));
                    }

                    break;
                case ServiceDiscoveryOperations.GetServices:
                    // The request is multicast, so that it reaches all servers on the host, which share the discovery
                    // port. Only the requested server answers.
                    if (serializer.Deserialize<RpcGetServicesRequest>(requestFrame.Payload) is RpcGetServicesRequest getRequest
                        && getRequest.ServerId == this.server.ServicePublisher.ServerId.ToGuid())
                    {
                        return WriteResponse(requestFrame, this.GetServices(getRequest, serviceProvider));
                    }

                    break;
            }

            return null;
        }

        private static byte[] WriteResponse<TResponse>(in LightweightRpcFrame requestFrame, TResponse response) where TResponse : class
        {
            // Discovery responses always echo the operation name, which identifies the response, and never return an
            // operation number.
            var responseHeader = new LightweightRpcFrame(RpcFrameType.UnaryResponse, requestFrame.MessageNumber, requestFrame.RpcOperation, null);

            using var frameWriter = new LightweightRpcFrameWriter(ServiceDiscoveryOperations.MaxDatagramSize);
            return frameWriter.WriteFrame(responseHeader, response, ServiceDiscoveryOperations.DiscoverySerializer);
        }

        private static int GetJsonStringSize(string text)
        {
            // The quotes, plus the escapes of the characters that are always escaped. The discovery serializer doesn't
            // escape non-ASCII characters.
            int size = ServiceDiscoveryOperations.GetUtf8Size(text) + 2;
            foreach (char c in text)
            {
                if (c == '"' || c == '\\')
                {
                    size += 1;
                }
                else if (c < 0x20)
                {
                    size += 5;
                }
            }

            return size;
        }

        private RpcFindServersResponse FindServers(RpcFindServersRequest request, IServiceProvider? serviceProvider, LightweightRpcEndPoint endPoint)
        {
            var services = this.GetDiscoverableServices(serviceProvider);

            List<int>? matches = null;
            if (request.Services is string[] requestedServices)
            {
                int count = Math.Min(requestedServices.Length, ServiceDiscoveryOperations.MaxRequestedServices);
                for (int index = 0; index < count; index++)
                {
                    if (requestedServices[index] is string serviceName && services.NameSet.Contains(serviceName))
                    {
                        (matches ??= new List<int>()).Add(index);
                    }
                }
            }

            var properties = this.server.ServerProperties;
            return new RpcFindServersResponse
            {
                ClientId = request.ClientId,
                ConnectionInfo = endPoint.GetConnectionInfo(this.server.ServicePublisher.ServerId),
                Properties = properties.Count > 0 ? new Dictionary<string, string>(properties) : null,
                Matches = matches?.ToArray(),
                ServiceCount = services.Names.Length,
                ServicesHash = services.Hash
            };
        }

        /// <summary>
        /// Gets the discoverable singleton services: the published singletons whose service allows discovery and whose
        /// name is at most <see cref="ServiceDiscoveryOperations.MaxServiceNameSize"/> bytes, sorted by their UTF-8 names.
        /// </summary>
        private DiscoverableServices GetDiscoverableServices(IServiceProvider? serviceProvider)
        {
            var singletonTypes = this.server.ServiceActivator.GetPublishedSingletons();
            lock (this.syncRoot)
            {
                // Retrieving the service options is not particularly fast, so the services are only updated when the
                // published singletons have changed.
                if (this.cachedServices != null && this.cachedSingletonTypes != null
                    && this.cachedSingletonTypes.SequenceEqual(singletonTypes))
                {
                    return this.cachedServices;
                }
            }

            var names = new List<(string Name, byte[] Utf8Name)>();
            foreach (var type in singletonTypes)
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
                        var utf8Name = Encoding.UTF8.GetBytes(serviceInfo.FullName);
                        if (utf8Name.Length <= ServiceDiscoveryOperations.MaxServiceNameSize)
                        {
                            names.Add((serviceInfo.FullName, utf8Name));
                        }
                    }
                }
            }

            names.Sort((x, y) => ServiceDiscoveryOperations.CompareUtf8(x.Utf8Name, y.Utf8Name));

            var services = new DiscoverableServices(
                names.Select(n => n.Name).ToArray(),
                ServiceDiscoveryOperations.ComputeServicesHash(names.Select(n => n.Utf8Name)));

            lock (this.syncRoot)
            {
                this.cachedSingletonTypes = singletonTypes;
                this.cachedServices = services;
            }

            return services;
        }

        private RpcGetServicesResponse GetServices(RpcGetServicesRequest request, IServiceProvider? serviceProvider)
        {
            var services = this.GetDiscoverableServices(serviceProvider);

            var response = new RpcGetServicesResponse
            {
                ClientId = request.ClientId,
                ServerId = this.server.ServicePublisher.ServerId.ToGuid(),
                ServiceCount = services.Names.Length,
                ServicesHash = services.Hash,
                Offset = request.Offset,
                Services = Array.Empty<string>()
            };

            if (request.Offset >= 0 && request.Offset < services.Names.Length)
            {
                // Add as many names as fit in the datagram, but at least one.
                int size = ServiceDiscoveryOperations.DiscoverySerializer.Serialize(response).Length;
                int maxSize = ServiceDiscoveryOperations.MaxDatagramSize - FrameHeaderAllowance;

                var page = new List<string>();
                for (int index = request.Offset; index < services.Names.Length; index++)
                {
                    string name = services.Names[index];
                    int nameSize = GetJsonStringSize(name) + (page.Count > 0 ? 1 : 0);
                    if (page.Count > 0 && size + nameSize > maxSize)
                    {
                        break;
                    }

                    page.Add(name);
                    size += nameSize;
                }

                response.Services = page.ToArray();
            }

            return response;
        }

        private sealed class DiscoverableServices
        {
            internal DiscoverableServices(string[] names, string hash)
            {
                this.Names = names;
                this.NameSet = new HashSet<string>(names, StringComparer.Ordinal);
                this.Hash = hash;
            }

            internal string Hash { get; }

            internal string[] Names { get; }

            internal HashSet<string> NameSet { get; }
        }
    }
}
