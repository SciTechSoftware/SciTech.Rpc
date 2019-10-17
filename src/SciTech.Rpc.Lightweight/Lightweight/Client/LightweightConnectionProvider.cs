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

//using Microsoft.Extensions.Options;
using SciTech.Rpc.Client;
using SciTech.Rpc.Lightweight.Client.Internal;
using System;

namespace SciTech.Rpc.Lightweight.Client
{
    public class LightweightConnectionProvider : IRpcConnectionProvider
    {
        public const string LightweightTcpScheme = "lightweight.tcp";

        public const string LightweightPipeScheme = "lightweight.pipe";

        private readonly LightweightOptions? lightweightOpions = null;

        private readonly ImmutableRpcClientOptions? options;

        private readonly IRpcProxyDefinitionsProvider? definitionsProvider;

        private readonly SslClientOptions? sslOptions;

        public LightweightConnectionProvider(
            IRpcClientOptions? options = null, 
            LightweightOptions? lightweightOpions = null,
            IRpcProxyDefinitionsProvider? definitionsProvider = null)
            : this(null, options, lightweightOpions, definitionsProvider)
        {
        }

        public LightweightConnectionProvider(
            SslClientOptions? sslOptions,
            IRpcClientOptions? options = null,
            LightweightOptions? lightweightOpions = null,
            IRpcProxyDefinitionsProvider? definitionsProvider = null)
        {
            this.sslOptions = sslOptions;
            this.definitionsProvider = definitionsProvider;
            this.options = options?.AsImmutable();
            this.lightweightOpions = lightweightOpions;
        }

        //public LightweightConnectionProvider(
        //    IOptions<RpcClientOptions> options,
        //    LightweightOptions? lightweightOpions = null,
        //    IRpcProxyDefinitionsProvider? definitionsProvider = null)
        //    : this(null, options?.Value, lightweightOpions, definitionsProvider)
        //{
        //}

        //public LightweightConnectionProvider(
        //    SslClientOptions? sslOptions,
        //    IOptions<RpcClientOptions> options,
        //    LightweightOptions? lightweightOpions = null,
        //    IRpcProxyDefinitionsProvider? definitionsProvider = null)
        //    : this(sslOptions, options?.Value, lightweightOpions, definitionsProvider)
        //{
        //}

        public bool CanCreateConnection(RpcServerConnectionInfo connectionInfo)
        {            
            return connectionInfo?.HostUrl?.Scheme is string scheme 
                &&  ( scheme == WellKnownRpcSchemes.LightweightTcp 
                || scheme == WellKnownRpcSchemes.LightweightPipe );
        }

        public IRpcChannel CreateConnection(RpcServerConnectionInfo connectionInfo, IRpcClientOptions? options, IRpcProxyDefinitionsProvider? definitionsProvider )
        {
            var scheme = connectionInfo?.HostUrl?.Scheme;
            if (scheme == LightweightTcpScheme)
            {
                var actualDefinitionsProvider = this.definitionsProvider ?? definitionsProvider;
                var proxyGenerator = LightweightProxyGenerator.Factory.CreateProxyGenerator(actualDefinitionsProvider);

                return new TcpRpcConnection(
                    connectionInfo!, this.sslOptions,
                    ImmutableRpcClientOptions.Combine(options, this.options),
                    proxyGenerator,
                    this.lightweightOpions);
            }

            if( scheme == WellKnownRpcSchemes.LightweightPipe)
            {
                var actualDefinitionsProvider = this.definitionsProvider ?? definitionsProvider;
                var proxyGenerator = LightweightProxyGenerator.Factory.CreateProxyGenerator(actualDefinitionsProvider);

                return new NamedPipeRpcConnection(
                    connectionInfo!, 
                    ImmutableRpcClientOptions.Combine(options, this.options),
                    proxyGenerator,
                    this.lightweightOpions);
            }

            throw new ArgumentException("Unsupported connection info. Use CanCreateConnection to check whether a connection can be created.");
        }
    }
}
