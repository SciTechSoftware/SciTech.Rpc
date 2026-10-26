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

using SciTech.Rpc.Client;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.NetGrpc.Client.Internal
{
    public class GrpcCallMetadata : IRpcRequestContext
    {
        private readonly GrpcCore.Metadata metadata;

        internal GrpcCallMetadata(GrpcCore.Metadata metadata)
        {
            this.metadata = metadata;
        }

        /// <summary>
        /// Cancellation by interceptors not (yet?) implemented.
        /// </summary>
        public CancellationToken CancellationToken => default;

        public void AddBinaryHeader(string key, IReadOnlyList<byte> value)
        {
            this.metadata.Add(key, value.ToArray());
        }

        public void AddHeader(string key, string value)
        {
            this.metadata.Add(key, value);
        }

        public ImmutableArray<byte> GetBinaryHeader(string key)
        {
            for (int i = 0; i < this.metadata.Count; i++)
            {
                var entry = metadata[i];
                if (entry.Key == key)
                {
                    return entry.ValueBytes.ToImmutableArray();
                }
            }

            return default;
        }

        public string? GetHeaderString(string key)
        {
            for (int i = 0; i < this.metadata.Count; i++)
            {
                var entry = metadata[i];
                if (entry.Key == key)
                {
                    return entry.Value;
                }
            }

            return null;
        }
    }
}
