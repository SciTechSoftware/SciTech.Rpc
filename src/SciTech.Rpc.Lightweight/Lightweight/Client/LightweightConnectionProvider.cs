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

        private readonly AuthenticationClientOptions? authenticationOptions;

        public LightweightConnectionProvider(
            IRpcClientOptions? options = null, 
            LightweightOptions? lightweightOpions = null)
            : this(null, options, lightweightOpions)
        {
        }

        public LightweightConnectionProvider(
            AuthenticationClientOptions? authenticationOptions,
            IRpcClientOptions? options = null,
            LightweightOptions? lightweightOpions = null)
        {
            this.authenticationOptions = authenticationOptions;
            this.options = options?.AsImmutable();
            this.lightweightOpions = lightweightOpions;
        }


        public bool CanCreateChannel(RpcServerConnectionInfo connectionInfo)
        {            
            return connectionInfo?.HostUrl?.Scheme is string scheme 
                &&  ( scheme == WellKnownRpcSchemes.LightweightTcp 
                || scheme == WellKnownRpcSchemes.LightweightPipe );
        }

        public IRpcChannel CreateChannel(RpcServerConnectionInfo connectionInfo, IRpcClientOptions? options)
        {
            var scheme = connectionInfo?.HostUrl?.Scheme;
            if (scheme == LightweightTcpScheme)
            {
                var proxyGenerator = LightweightProxyGenerator.Default;

                return new TcpRpcConnection(
                    connectionInfo!, 
                    this.authenticationOptions, 
                    ImmutableRpcClientOptions.Combine(options, this.options),
                    proxyGenerator,
                    this.lightweightOpions);
            }

            if( scheme == WellKnownRpcSchemes.LightweightPipe)
            {
                var proxyGenerator = LightweightProxyGenerator.Default;

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
