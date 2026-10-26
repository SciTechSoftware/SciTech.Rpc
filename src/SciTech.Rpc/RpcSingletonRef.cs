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
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Text;

namespace SciTech.Rpc
{
    /// <summary>
    /// Provides information about how to access a published RPC service.
    /// </summary>
    /// <typeparam name="TService"></typeparam>
    [DataContract]
    //[ProtoContract(SkipConstructor = true)]
    public class RpcSingletonRef<TService> : RpcSingletonRef where TService : class
    {
        public RpcSingletonRef() { }

        internal RpcSingletonRef(RpcConnectionInfo? connectionInfo) : base(connectionInfo)
        {
        }

        // Redeclared, since the Protobuf serializer ignores the members of the base type (which is not declared as a sub-type).
        [DataMember(Order = 1)]
        [JsonInclude]
        public new RpcConnectionInfo? ServerConnection { get => base.ServerConnection; protected set => base.ServerConnection = value; }
    }

    /// <summary>
    /// Provides information about how to access a published RPC service.
    /// </summary>
    [DataContract]
    //[ProtoContract(SkipConstructor = true)]
    public class RpcSingletonRef
    {
        public RpcSingletonRef() { }

        internal RpcSingletonRef(RpcConnectionInfo? connectionInfo)
        {
            this.ServerConnection = connectionInfo;
        }

        [DataMember(Order = 1)]
        [JsonInclude]
        public RpcConnectionInfo? ServerConnection { get; protected set; }

        public RpcSingletonRef<TOtherService> Cast<TOtherService>() where TOtherService : class
        {
            return new RpcSingletonRef<TOtherService>(this.ServerConnection);
        }
    }
}
