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

//using Microsoft.Extensions.Options;
using SciTech.Rpc.Client;
using SciTech.Rpc.Lightweight.Client.Internal;
using System;

namespace SciTech.Rpc.Lightweight.Client
{
    public class LightweightConnectionProvider : IRpcConnectionProvider
    {
        private readonly LightweightOptions? lightweightOpions = null;

        private readonly ImmutableRpcClientOptions? options;

        private readonly AuthenticationClientOptions? authenticationOptions;

        private readonly Func<RpcConnectionInfo,AuthenticationClientOptions?>? authenticationOptionsProvider;

        public LightweightConnectionProvider(
            IRpcClientOptions? options = null, 
            LightweightOptions? lightweightOpions = null)
            : this(null, null, options, lightweightOpions)
        {
        }

        public LightweightConnectionProvider(
            AuthenticationClientOptions authenticationOptions,
            IRpcClientOptions? options = null,
            LightweightOptions? lightweightOpions = null)
            : this(authenticationOptions, null, options, lightweightOpions)
        {
        }

        public LightweightConnectionProvider(
            Func<RpcConnectionInfo, AuthenticationClientOptions?> authenticationOptionsProvider,
            IRpcClientOptions? options = null,
            LightweightOptions? lightweightOpions = null)
            : this(null, authenticationOptionsProvider, options, lightweightOpions)
        {
        }

        private LightweightConnectionProvider(
            AuthenticationClientOptions? authenticationOptions,
            Func<RpcConnectionInfo, AuthenticationClientOptions?>? authenticationOptionsProvider,
            IRpcClientOptions? options = null,
            LightweightOptions? lightweightOpions = null)
        {
            this.authenticationOptions = authenticationOptions;
            this.authenticationOptionsProvider = authenticationOptionsProvider;
            this.options = options?.AsImmutable();
            this.lightweightOpions = lightweightOpions;
        }

        public bool CanCreateChannel(RpcConnectionInfo connectionInfo)
        {            
            return connectionInfo?.HostUrl?.Scheme is string scheme 
                &&  ( scheme == WellKnownRpcSchemes.LightweightTcp 
                || scheme == WellKnownRpcSchemes.LightweightPipe );
        }

        public IRpcChannel CreateChannel(RpcConnectionInfo connectionInfo, IRpcClientOptions? options)
        {
            var scheme = connectionInfo?.HostUrl?.Scheme;
            if (scheme == WellKnownRpcSchemes.LightweightTcp)
            {
                var proxyGenerator = LightweightProxyGenerator.Default;

                return new TcpRpcConnection(
                    connectionInfo!, 
                    this.authenticationOptionsProvider?.Invoke(connectionInfo!) ?? this.authenticationOptions, 
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
