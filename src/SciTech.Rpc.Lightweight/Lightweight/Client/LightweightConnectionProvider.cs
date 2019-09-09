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
using System;

namespace SciTech.Rpc.Lightweight.Client
{
    public class LightweightConnectionProvider : IRpcConnectionProvider
    {
        public const string LightweightTcpScheme = "lightweight.tcp";

        private readonly LightweightOptions? lightweightOpions = null;

        private readonly ImmutableRpcClientOptions? options;

        private readonly LightweightProxyProvider proxyProvider;

        private readonly SslClientOptions? sslOptions;

        public LightweightConnectionProvider(
            ImmutableRpcClientOptions? options = null, LightweightOptions? lightweightOpions = null,
            LightweightProxyProvider? proxyProvider = null)
            : this(null, options, lightweightOpions, proxyProvider)
        {
        }

        public LightweightConnectionProvider(
            SslClientOptions? sslOptions,
            ImmutableRpcClientOptions? options = null,
            LightweightOptions? lightweightOpions = null,
            LightweightProxyProvider? proxyProvider = null)
        {
            this.sslOptions = sslOptions;
            this.proxyProvider = proxyProvider ?? LightweightProxyProvider.Default;
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
                    return new TcpLightweightRpcConnection(connectionInfo, this.sslOptions,
                        ImmutableRpcClientOptions.Combine(options, this.options),
                        this.proxyProvider,
                        this.lightweightOpions);
                }
            }

            throw new ArgumentException("Unsupported connection info. Use CanCreateConnection to check whether a connection can be created.");
        }
    }
}
