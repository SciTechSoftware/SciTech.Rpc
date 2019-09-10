#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using SciTech.Rpc.Client;
using SciTech.Rpc.Lightweight.Client.Internal;
using System;

namespace SciTech.Rpc.Lightweight.Client
{
    public class LightweightConnectionProvider : IRpcConnectionProvider
    {
        public const string LightweightTcpScheme = "lightweight.tcp";

        private readonly LightweightOptions? lightweightOpions = null;

        private readonly ImmutableRpcClientOptions? options;

        private readonly LightweightProxyGenerator proxyGenerator;

        private readonly SslClientOptions? sslOptions;

        public LightweightConnectionProvider(
            ImmutableRpcClientOptions? options = null, LightweightOptions? lightweightOpions = null,
            IRpcProxyDefinitionsProvider? definitionsProvider = null)
            : this(null, options, lightweightOpions, definitionsProvider)
        {
        }

        public LightweightConnectionProvider(
            SslClientOptions? sslOptions,
            ImmutableRpcClientOptions? options = null,
            LightweightOptions? lightweightOpions = null,
            IRpcProxyDefinitionsProvider? definitionsProvider = null)
        {
            this.sslOptions = sslOptions;
            this.proxyGenerator = LightweightProxyGenerator.Factory.CreateProxyGenerator(definitionsProvider);
            this.options = options;
            this.lightweightOpions = lightweightOpions;
        }

        public bool CanCreateConnection(RpcServerConnectionInfo connectionInfo)
        {
            if (Uri.TryCreate(connectionInfo?.HostUrl, UriKind.Absolute, out var parsedUrl))
            {
                if (parsedUrl.Scheme == LightweightTcpScheme)
                {
                    return true;
                }
            }

            return false;
        }

        public IRpcServerConnection CreateConnection(RpcServerConnectionInfo connectionInfo, ImmutableRpcClientOptions? options)
        {
            if (connectionInfo != null && Uri.TryCreate(connectionInfo.HostUrl, UriKind.Absolute, out var parsedUrl))
            {
                if (parsedUrl.Scheme == LightweightTcpScheme)
                {
                    return new TcpLightweightRpcConnection(
                        connectionInfo, this.sslOptions,
                        ImmutableRpcClientOptions.Combine(options, this.options),
                        this.proxyGenerator,
                        this.lightweightOpions);
                }
            }

            throw new ArgumentException("Unsupported connection info. Use CanCreateConnection to check whether a connection can be created.");
        }
    }
}
