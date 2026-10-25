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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Security.Principal;

namespace SciTech.Rpc.Server
{
    public class EstablishedConnection
    {
        public static readonly EstablishedConnection Empty = new EstablishedConnection(null, null, null, null, null, null);

        public EstablishedConnection(IPrincipal? user, string? remoteName, bool? isMutuallyAuthenticated, bool? isEncrypted, bool? isSigned, IReadOnlyCollection<KeyValuePair<string, ImmutableArray<byte>>>? headers)
        {
            this.User = user;
            this.RemoteName = remoteName;
            this.IsMutuallyAuthenticated = isMutuallyAuthenticated;
            this.IsEncrypted = isEncrypted;
            this.IsSigned = isSigned;
            this.Headers = headers;
        }

        public IPrincipal? User { get; }

        public string? RemoteName { get; }

        public bool? IsMutuallyAuthenticated { get; }

        public bool? IsEncrypted { get; }

        public bool? IsSigned { get; }

        public IReadOnlyCollection<KeyValuePair<string, ImmutableArray<byte>>>? Headers { get; }
    }

}