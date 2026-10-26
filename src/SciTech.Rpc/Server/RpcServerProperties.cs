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

using System;
using System.Collections.Generic;
using System.Text;

namespace SciTech.Rpc.Server
{
    /// <summary>
    /// Defines the limits of the server properties (<see cref="IRpcServerOptions.ServerProperties"/>). The properties are
    /// included in discovery responses, which must fit in a single unfragmented datagram.
    /// </summary>
    public static class RpcServerProperties
    {
        /// <summary>
        /// The maximum number of server properties.
        /// </summary>
        public const int MaxCount = 8;

        /// <summary>
        /// The maximum total size, in UTF-8 bytes, of all keys and values of the server properties.
        /// </summary>
        public const int MaxTotalSize = 256;

        /// <summary>
        /// Checks that <paramref name="properties"/> are within the limits.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown if a key is empty, a value is <c>null</c>, or the properties exceed the limits.</exception>
        public static void Validate(IReadOnlyDictionary<string, string> properties)
        {
            if (properties is null) throw new ArgumentNullException(nameof(properties));

            if (properties.Count > MaxCount)
            {
                throw new ArgumentException($"At most {MaxCount} server properties can be specified.", nameof(properties));
            }

            int totalSize = 0;
            foreach (var pair in properties)
            {
                if (string.IsNullOrEmpty(pair.Key))
                {
                    throw new ArgumentException("A server property key must not be empty.", nameof(properties));
                }

                if (pair.Value == null)
                {
                    throw new ArgumentException($"The value of server property '{pair.Key}' must not be null.", nameof(properties));
                }

                totalSize += Encoding.UTF8.GetByteCount(pair.Key) + Encoding.UTF8.GetByteCount(pair.Value);
            }

            if (totalSize > MaxTotalSize)
            {
                throw new ArgumentException($"The keys and values of the server properties must not exceed {MaxTotalSize} UTF-8 bytes in total.", nameof(properties));
            }
        }
    }
}
