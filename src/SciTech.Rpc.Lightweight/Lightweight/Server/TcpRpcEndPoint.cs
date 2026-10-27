#region Copyright notice and license

// Copyright (c) SciTech Software AB and contributors.
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

        /// <summary>
        /// Initializes an end point that listens on <paramref name="port"/>, either on all network interfaces, or on the
        /// address of <paramref name="hostName"/>.
        /// </summary>
        /// <param name="hostName">The host name that clients use to connect to the server. It's provided in the connection
        /// info of the end point, e.g. by discovery. If <paramref name="bindToAllInterfaces"/> is <c>false</c>, it's also
        /// the address to listen on, which is not recommended for a host name (see the remarks).</param>
        /// <param name="port">The TCP port to listen on.</param>
        /// <param name="bindToAllInterfaces"><c>true</c> to listen on all network interfaces, for both IPv4 and IPv6, e.g.
        /// to allow connections from other machines. <c>false</c> to only listen on the address of <paramref name="hostName"/>.</param>
        /// <param name="authenticationOptions">Supported authentication options. If <c>null</c>, then anonymous authentication will be used.</param>
        /// <remarks>
        /// <para>
        /// Using a host name as the address to listen on, i.e. with <paramref name="bindToAllInterfaces"/> <c>false</c>, is
        /// not recommended. A host name may resolve to several addresses, but the end point only listens on one of them:
        /// an IPv4 address if there is one, otherwise an IPv6 address. E.g. "localhost" only listens on 127.0.0.1, not on
        /// ::1. Instead, provide the IP address to listen on with
        /// <see cref="TcpRpcEndPoint(string, string, int, bool, AuthenticationServerOptions?)"/>, e.g. "127.0.0.1" to only
        /// allow connections from the local machine, or listen on all network interfaces.
        /// </para>
        /// <para>
        /// An end point that allows connections from other machines should require authentication, e.g. with
        /// <see cref="SslServerOptions"/> or <see cref="NegotiateServerOptions"/>.
        /// </para>
        /// </remarks>
        public TcpRpcEndPoint(string hostName, int port, bool bindToAllInterfaces, AuthenticationServerOptions? authenticationOptions = null)
            : this(hostName, CreateNetEndPoint(hostName, port, bindToAllInterfaces), CreateAuthenticationOptions(authenticationOptions))
        {
        }

        /// <summary>
        /// Initializes an end point that listens on <paramref name="port"/>, either on all network interfaces, or on
        /// <paramref name="endPointAddress"/>.
        /// </summary>
        /// <param name="hostName">The host name that clients use to connect to the server. It's provided in the connection
        /// info of the end point, e.g. by discovery.</param>
        /// <param name="endPointAddress">The IP address to listen on, e.g. "127.0.0.1", if <paramref name="bindToAllInterfaces"/>
        /// is <c>false</c>. A host name is also accepted, but not recommended (see the remarks of
        /// <see cref="TcpRpcEndPoint(string, int, bool, AuthenticationServerOptions?)"/>).</param>
        /// <param name="port">The TCP port to listen on.</param>
        /// <param name="bindToAllInterfaces"><c>true</c> to listen on all network interfaces, for both IPv4 and IPv6, e.g.
        /// to allow connections from other machines. <c>false</c> to only listen on <paramref name="endPointAddress"/>.</param>
        /// <param name="authenticationOptions">Supported authentication options. If <c>null</c>, then anonymous authentication will be used.</param>
        public TcpRpcEndPoint(string hostName, string endPointAddress, int port, bool bindToAllInterfaces, AuthenticationServerOptions? authenticationOptions = null)
            : this(hostName, CreateNetEndPoint(endPointAddress, port, bindToAllInterfaces), CreateAuthenticationOptions(authenticationOptions))
        {
        }

        /// <summary>
        /// Initializes an end point that listens on <paramref name="endPoint"/>.
        /// </summary>
        /// <param name="hostName">The host name that clients use to connect to the server. It's provided in the connection
        /// info of the end point, e.g. by discovery.</param>
        /// <param name="endPoint">The IP address and port to listen on.</param>
        /// <param name="authenticationOptions">Supported authentication options. If <c>null</c>, then anonymous authentication will be used.</param>
        public TcpRpcEndPoint(string hostName, IPEndPoint endPoint, AuthenticationServerOptions? authenticationOptions = null)
            : this(hostName, endPoint, CreateAuthenticationOptions(authenticationOptions))
        {
        }

        /// <inheritdoc cref="TcpRpcEndPoint(string, int, bool, AuthenticationServerOptions?)"/>
        /// <param name="hostName">The host name that clients use to connect to the server. It's provided in the connection
        /// info of the end point, e.g. by discovery. If <paramref name="bindToAllInterfaces"/> is <c>false</c>, it's also
        /// the address to listen on, which is not recommended for a host name (see the remarks).</param>
        /// <param name="port">The TCP port to listen on.</param>
        /// <param name="bindToAllInterfaces"><c>true</c> to listen on all network interfaces, for both IPv4 and IPv6, e.g.
        /// to allow connections from other machines. <c>false</c> to only listen on the address of <paramref name="hostName"/>.</param>
        /// <param name="authenticationOptions">Supported authentication options. If <c>null</c> or empty, then anonymous authentication will be used.</param>
        public TcpRpcEndPoint(string hostName, int port, bool bindToAllInterfaces, IReadOnlyCollection<AuthenticationServerOptions>? authenticationOptions)
            : this(hostName, CreateNetEndPoint(hostName, port, bindToAllInterfaces), authenticationOptions)
        {
        }

        /// <inheritdoc cref="TcpRpcEndPoint(string, string, int, bool, AuthenticationServerOptions?)"/>
        /// <param name="hostName">The host name that clients use to connect to the server. It's provided in the connection
        /// info of the end point, e.g. by discovery.</param>
        /// <param name="endPointAddress">The IP address to listen on, e.g. "127.0.0.1", if <paramref name="bindToAllInterfaces"/>
        /// is <c>false</c>. A host name is also accepted, but not recommended (see the remarks of
        /// <see cref="TcpRpcEndPoint(string, int, bool, AuthenticationServerOptions?)"/>).</param>
        /// <param name="port">The TCP port to listen on.</param>
        /// <param name="bindToAllInterfaces"><c>true</c> to listen on all network interfaces, for both IPv4 and IPv6, e.g.
        /// to allow connections from other machines. <c>false</c> to only listen on <paramref name="endPointAddress"/>.</param>
        /// <param name="authenticationOptions">Supported authentication options. If <c>null</c> or empty, then anonymous authentication will be used.</param>
        public TcpRpcEndPoint(string hostName, string endPointAddress, int port, bool bindToAllInterfaces, IReadOnlyCollection<AuthenticationServerOptions>? authenticationOptions)
            : this(hostName, CreateNetEndPoint(endPointAddress, port, bindToAllInterfaces), authenticationOptions)
        {
        }

        /// <summary>
        /// Initializes an end point that listens on <paramref name="endPoint"/>.
        /// </summary>
        /// <param name="hostName">The host name that clients use to connect to the server. It's provided in the connection
        /// info of the end point, e.g. by discovery.</param>
        /// <param name="endPoint">The IP address and port to listen on.</param>
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
                // A host name may resolve to several addresses, but only one is listened on. This is not recommended
                // (see the constructor documentation), but it's deterministic: an IPv4 address is preferred, since
                // TcpRpcConnection connects to the IPv4 address of a host name. E.g. "localhost" resolves to ::1 first
                // on Windows, and listening on ::1 would make the server unreachable for a client using "localhost".
                var addresses = Dns.GetHostAddresses(serverAddress);

                if (addresses?.Length > 0)
                {
                    var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                    var usableAddresses = addresses
                        .Where(address => networkInterfaces
                            .FirstOrDefault(i => i.GetIPProperties().UnicastAddresses.Any(a => Equals(a.Address, address)))
                            ?.OperationalStatus == OperationalStatus.Up)
                        .ToList();

                    var selectedAddress = usableAddresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                        ?? usableAddresses.FirstOrDefault();
                    if (selectedAddress != null)
                    {
                        return new IPEndPoint(selectedAddress, port);
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
                return this.connectionHandler.RunPipelineClientAsync(client.Transport, this.rpcEndPoint, client.EstablishedConnection);
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