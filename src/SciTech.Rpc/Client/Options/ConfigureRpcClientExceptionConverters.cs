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

using Microsoft.Extensions.Options;
using SciTech.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using MSOptions = Microsoft.Extensions.Options.Options;

namespace SciTech.Rpc.Client.Options
{
    public class ConfigureRpcClientExceptionConverters : IConfigureNamedOptions<RpcClientOptions>
    {
        private readonly ImmutableArrayList<IRpcClientExceptionConverter> exceptionConverters;
        private readonly string? name;

        public ConfigureRpcClientExceptionConverters(string? name, IEnumerable<IRpcClientExceptionConverter> exceptionConverters)
        {
            this.name = name;
            this.exceptionConverters = exceptionConverters.ToImmutableArrayList();
        }

        public void Configure(RpcClientOptions options)
        {
            this.Configure(MSOptions.DefaultName, options);
        }

        public void Configure(string? name, RpcClientOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            if (this.name == null || this.name == name)
            {
                options.ExceptionConverters.AddRange(this.exceptionConverters);
            }
        }
    }
}