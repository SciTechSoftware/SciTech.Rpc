#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace SciTech.Rpc
{
    /// <summary>
    /// Provides information about how to access a published RPC object for the service defined by <typeparamref name="TService"/>.
    /// </summary>
    /// <typeparam name="TService">Type of the service definition interface</typeparam>
    [DataContract]
    [Serializable]
    public class RpcObjectRef<TService> : RpcObjectRef where TService : class
    {
        public RpcObjectRef() { }

        internal RpcObjectRef(RpcConnectionInfo? connectionInfo, RpcObjectId objectId, string[]? implementedServices) : base(connectionInfo, objectId, implementedServices)
        {
        }

        [DataMember(Order = 3)]
#pragma warning disable CA1819 // Properties should not return arrays
        public new string[]? ImplementedServices { get => base.ImplementedServices; protected set => base.ImplementedServices = value; }
#pragma warning restore CA1819 // Properties should not return arrays

        [DataMember(Order = 1)]
        public new RpcObjectId ObjectId { get => base.ObjectId; protected set => base.ObjectId = value; }

        [DataMember(Order = 2)]
        public new RpcConnectionInfo? ServerConnection { get => base.ServerConnection; protected set => base.ServerConnection = value; }
    }

    /// <summary>
    /// Provides information about how to access a published RPC object.
    /// </summary>
    [DataContract]
    [Serializable]
    public class RpcObjectRef : IEquatable<RpcObjectRef>
    {
        public RpcObjectRef() { }

        internal RpcObjectRef(RpcConnectionInfo? connectionInfo, RpcObjectId objectId, string[]? implementedServices)
        {
            this.ServerConnection = connectionInfo;
            this.ObjectId = objectId;
            this.ImplementedServices = implementedServices;
        }

        [DataMember(Order = 3)]
#pragma warning disable CA1819 // Properties should not return arrays
        public string[]? ImplementedServices { get; set; }
#pragma warning restore CA1819 // Properties should not return arrays

        [DataMember(Order = 1)]
        public RpcObjectId ObjectId { get; set; }

        [DataMember(Order = 2)]
        public RpcConnectionInfo? ServerConnection { get; set; }

        public RpcObjectRef<TService> Cast<TService>() where TService : class
        {
            if (this.GetType() != typeof(RpcObjectRef<TService>))
            {
                return new RpcObjectRef<TService>(this.ServerConnection, this.ObjectId, this.ImplementedServices);
            }

            return (RpcObjectRef<TService>)this;
        }

        public RpcObjectRef Cast()
        {
            if (this.GetType() != typeof(RpcObjectRef))
            {
                return new RpcObjectRef(this.ServerConnection, this.ObjectId, this.ImplementedServices);
            }

            return this;
        }

        public bool Equals([AllowNull]RpcObjectRef other)
        {
            return other != null && this.ObjectId == other.ObjectId;
        }

        public sealed override bool Equals(object? obj)
        {
            return obj is RpcObjectRef other && this.Equals(other);
        }

        public override int GetHashCode()
        {
            return this.ObjectId.GetHashCode();
        }
    }
}
