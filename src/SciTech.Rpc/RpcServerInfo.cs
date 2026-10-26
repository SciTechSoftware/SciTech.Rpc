#region Copyright notice and license
// Copyright (c) SciTech Software AB and contributors.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License.
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using SciTech.Collections.Immutable;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace SciTech.Rpc
{
    /// <summary>
    /// Information about an RPC server, retrieved over a connection to the server using
    /// <see cref="Client.RpcChannelExtensions.GetServerInfoAsync"/>. Unlike the information provided by discovery, it is
    /// retrieved over an authenticated connection, so it can be used to check whether the server is compatible.
    /// Instances are immutable.
    /// </summary>
    public sealed class RpcServerInfo
    {
        internal RpcServerInfo(RpcConnectionInfo connectionInfo, IReadOnlyDictionary<string, string> properties, IReadOnlyList<string> services)
        {
            this.ConnectionInfo = connectionInfo;
            this.Properties = properties;
            this.Services = services;
        }

        /// <summary>
        /// Gets the connection info of the server, e.g. its id and display name.
        /// </summary>
        public RpcConnectionInfo ConnectionInfo { get; }

        /// <summary>
        /// Gets the display name of the server.
        /// </summary>
        public string DisplayName => this.ConnectionInfo.DisplayName;

        /// <summary>
        /// Gets the server properties, as defined by the server application (see <c>RpcServerOptions.ServerProperties</c>).
        /// The dictionary is immutable.
        /// </summary>
        public IReadOnlyDictionary<string, string> Properties { get; }

        /// <summary>
        /// Gets the id of the server.
        /// </summary>
        public RpcServerId ServerId => this.ConnectionInfo.ServerId;

        /// <summary>
        /// Gets the names of the singleton services published by the server, sorted by name, when the info was retrieved.
        /// Unlike discovery, it includes singletons that don't allow discovery. The services implemented by an object are
        /// provided by its proxy (see <see cref="Client.IRpcProxy.TryCastAsync{TService}"/>). The list is immutable.
        /// </summary>
        public IReadOnlyList<string> Services { get; }

        internal static RpcServerInfo FromResponse(Internal.RpcServerInfoResponse? response, RpcConnectionInfo channelConnectionInfo)
        {
            var services = (response?.Services ?? Array.Empty<string>())
                .Where(s => !string.IsNullOrEmpty(s))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToImmutableArrayList();

            return new RpcServerInfo(
                response?.ConnectionInfo ?? channelConnectionInfo,
                response?.Properties?.ToFrozenDictionary(StringComparer.Ordinal) ?? FrozenDictionary<string, string>.Empty,
                services);
        }

        public override string ToString() => this.ConnectionInfo.ToString();
    }
}
