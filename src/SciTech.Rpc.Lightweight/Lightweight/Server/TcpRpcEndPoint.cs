#region Copyright notice and license

// Copyright (c) 2019-2021, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License.
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//

#endregion Copyright notice and license

using SciTech.Collections;
using SciTech.Collections.Immutable;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Lightweight.Server.Internal;
using SciTech.Rpc.Server;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server
{
    /// <summary>
    /// Represents a lightweigth RPC endpoint based on the TCP protocol. It supports a high-performance direct
    /// TCP connection between the client and server. Authentication is provided using one or more <see cref="AuthenticationServerOptions"/>,
    /// such as SSL authentication, Negotiate authentication (Windows only), and anonymous authentication.
    /// </summary>
    public class TcpRpcEndPoint : LightweightRpcEndPoint
    {
        private static readonly ImmutableArrayList<AuthenticationServerOptions> AnonymousAuthenticationOptions
            = ImmutableArrayList.Create<AuthenticationServerOptions>(AnonymousAuthenticationServerOptions.Instance);

        private readonly ImmutableArrayList<AuthenticationServerOptions> authenticationOptions;

        public TcpRpcEndPoint(string hostName, int port, bool bindToAllInterfaces, AuthenticationServerOptions? authenticationOptions = null)
            : this(hostName, CreateNetEndPoint(hostName, port, bindToAllInterfaces), CreateAuthenticationOptions(authenticationOptions))
        {
        }

        public TcpRpcEndPoint(string hostName, string endPointAddress, int port, bool bindToAllInterfaces, AuthenticationServerOptions? authenticationOptions = null)
            : this(hostName, CreateNetEndPoint(endPointAddress, port, bindToAllInterfaces), CreateAuthenticationOptions(authenticationOptions))
        {
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="hostName"></param>
        /// <param name="endPoint"></param>
        /// <param name="authenticationOptions">Supported authentication options. If <c>null</c>, then anonymous authentication will be used.</param>
        public TcpRpcEndPoint(string hostName, IPEndPoint endPoint, AuthenticationServerOptions? authenticationOptions = null)
            : this(hostName, endPoint, CreateAuthenticationOptions(authenticationOptions))
        {
        }

        public TcpRpcEndPoint(string hostName, int port, bool bindToAllInterfaces, IReadOnlyCollection<AuthenticationServerOptions>? authenticationOptions)
            : this(hostName, CreateNetEndPoint(hostName, port, bindToAllInterfaces), authenticationOptions)
        {
        }

        public TcpRpcEndPoint(string hostName, string endPointAddress, int port, bool bindToAllInterfaces, IReadOnlyCollection<AuthenticationServerOptions>? authenticationOptions)
            : this(hostName, CreateNetEndPoint(endPointAddress, port, bindToAllInterfaces), authenticationOptions)
        {
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="hostName"></param>
        /// <param name="endPoint"></param>
        /// <param name="authenticationOptions">Supported authentication options. If <c>null</c> or empty, then anonymous authentication will be used.</param>
        public TcpRpcEndPoint(string hostName, IPEndPoint endPoint, IReadOnlyCollection<AuthenticationServerOptions>? authenticationOptions = null)
        {
            this.HostName = hostName ?? throw new ArgumentNullException(nameof(hostName));
            this.DisplayName = hostName;
            this.EndPoint = endPoint;

            this.authenticationOptions = authenticationOptions?.Count > 0 ? authenticationOptions.ToImmutableArrayList() : AnonymousAuthenticationOptions;
            if (this.authenticationOptions.Any(o => o == null)) throw new ArgumentNullException(nameof(authenticationOptions));
        }

        public override string DisplayName { get; }

        public IPEndPoint EndPoint { get; }

        public override string HostName { get; }

        public int Port => this.EndPoint.Port;

        public override RpcConnectionInfo GetConnectionInfo(RpcServerId serverId)
        {
            return new RpcConnectionInfo(this.DisplayName, new Uri($"lightweight.tcp://{this.HostName}:{this.Port}"), serverId);
        }

        protected internal override ILightweightRpcListener CreateListener(
            IRpcConnectionHandler connectionHandler,
            int maxRequestSize, int maxResponseSize)
        {
            return new RpcSslSocketServer(this, connectionHandler, maxRequestSize, this.authenticationOptions);
        }

        private static IReadOnlyCollection<AuthenticationServerOptions> CreateAuthenticationOptions(AuthenticationServerOptions? authenticationOptions)
        {
            if (authenticationOptions != null)
            {
                return ImmutableArrayList.Create(authenticationOptions);
            }

            return AnonymousAuthenticationOptions;
        }

        private static IPEndPoint CreateNetEndPoint(string serverAddress, int port, bool bindToAllInterfaces)
        {
            if (bindToAllInterfaces)
            {
                return new IPEndPoint(Socket.OSSupportsIPv6 ? IPAddress.IPv6Any : IPAddress.Any, port);
            }
            else if (IPAddress.TryParse(serverAddress, out var ipAddress))
            {
                return new IPEndPoint(ipAddress, port);
            }
            else
            {
                // TODO: Should this really be allowed? If there's more than one matching address
                // it's not possible to know which one is selected.
                var addresses = Dns.GetHostAddresses(serverAddress);

                if (addresses?.Length > 0)
                {
                    var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                    foreach (var address in addresses)
                    {
                        var ni = networkInterfaces.FirstOrDefault(i => i.GetIPProperties().UnicastAddresses.Any(a => Equals(a.Address, address)));
                        if (ni?.OperationalStatus == OperationalStatus.Up)
                        {
                            // This interface looks usable. (But maybe we should see if there's more than one usable interface?)
                            return new IPEndPoint(address, port);
                        }
                    }
                }

                throw new IOException($"Failed to lookup IP address for '{serverAddress}'.");
            }
        }


        private class RpcSslSocketServer : AuthenticatedSocketServer, ILightweightRpcListener
        {
            private const int MaxConnectionFrameSize = 65536;

            private readonly IRpcConnectionHandler connectionHandler;

            private readonly int maxRequestSize;
            private readonly TcpRpcEndPoint rpcEndPoint;
            private ImmutableArrayList<AuthenticationServerOptions> authenticationOptions;

            /// <summary>
            /// Create a new instance of a socket server
            /// </summary>
            internal RpcSslSocketServer(TcpRpcEndPoint rpcEndPoint, IRpcConnectionHandler connectionHandler, int maxRequestSize, ImmutableArrayList<AuthenticationServerOptions> authenticationOptions)
            {
                this.connectionHandler = connectionHandler;
                this.rpcEndPoint = rpcEndPoint;
                this.maxRequestSize = maxRequestSize;
                this.authenticationOptions = authenticationOptions;
            }

            public ValueTask DisposeAsync()
            {
                this.Stop();

                return default;
            }

            public void Listen()
            {
                this.Listen(this.rpcEndPoint.EndPoint);
            }

            public Task StopAsync()
            {
                this.Stop();

                return Task.CompletedTask;
            }

            protected async override Task<AuthenticationServerOptions> GetAuthenticationOptionsAsync(Stream connectedClientStream, CancellationToken cancellationToken)
            {
                AuthenticationServerOptions? selectedOptions = null;

                LightweightRpcFrame connectionFrame = await LightweightRpcFrame.ReadFrameAsync(connectedClientStream, MaxConnectionFrameSize, cancellationToken).ContextFree();

                if (connectionFrame.FrameType == RpcFrameType.ConnectionRequest)
                {
                    // TODO: Should additional frame data be verified (e.g. operation name, messsage id, payload)?
                    var authenticationSchemesString = connectionFrame.GetHeaderString(WellKnownHeaderKeys.AuthenticationScheme);
                    if (string.IsNullOrEmpty(authenticationSchemesString))
                    {
                        authenticationSchemesString = AnonymousAuthenticationServerOptions.Instance.Name;
                    }

                    var authenticationSchemes = authenticationSchemesString!.Split(';');
                    foreach (var supportedScheme in this.authenticationOptions)
                    {
                        if (Array.Find(authenticationSchemes, s => supportedScheme.Name.Equals(s, StringComparison.OrdinalIgnoreCase)) != null)
                        {
                            selectedOptions = supportedScheme;
                            break;
                        }
                    }

                    using var frameWriter = new LightweightRpcFrameWriter(MaxConnectionFrameSize);

                    var responseHeaders = new List<KeyValuePair<string, ImmutableArray<byte>>>
                    {
                        new KeyValuePair<string, ImmutableArray<byte>>(WellKnownHeaderKeys.AuthenticationScheme, Rpc.Client.RpcRequestContext.ToHeaderBytes(selectedOptions?.Name ?? "" )),
                    };

                    var frameData = frameWriter.WriteFrame(
                        new LightweightRpcFrame(
                            RpcFrameType.ConnectionResponse,
                            0, "",
                            responseHeaders));

                    await connectedClientStream.WriteAsync(frameData, 0, frameData.Length, cancellationToken).ContextFree();
                }
                else
                {
                    throw new InvalidDataException("Unexpected connection frame.");
                }

                if (selectedOptions == null)
                {
                    throw new AuthenticationException("No matching authentication scheme found");
                }

                return selectedOptions;
            }

            protected override Task OnClientConnectedAsync(in ClientConnection client)
            {
                // TODO: Implement CancellationToken
                return this.connectionHandler.RunPipelineClientAsync(client.Transport, this.rpcEndPoint, client.User);
            }

            protected override void OnClientFaulted(in ClientConnection client, Exception exception)
            {
                base.OnClientFaulted(client, exception);
            }

            protected override void OnServerFaulted(Exception exception)
            {
                base.OnServerFaulted(exception);
            }
        }
    }
}