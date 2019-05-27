#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using System;
using System.Net;
using System.Runtime.Serialization;

namespace SciTech.Rpc
{
    /// <summary>
    /// The RpcServerId struct is used to identify a server process.
    /// </summary>
    [DataContract]
    [Serializable]
    public struct RpcServerId : IEquatable<RpcServerId>
    {
        public static readonly RpcServerId Empty;

        [DataMember(Order = 1, Name = "Id")]
        private Guid id;

        public RpcServerId(string idString)
        {
            this.id = new Guid(idString);
        }

        public RpcServerId(Guid id)
        {
            this.id = id;
        }

        public static RpcServerId NewId()
        {
            return new RpcServerId(Guid.NewGuid());
        }

        public override bool Equals(object obj)
        {
            return obj is RpcServerId other && this.Equals(other);
        }

        public bool Equals(RpcServerId other)
        {
            return this.id == other.id;
        }

        public override int GetHashCode()
        {
            return this.id.GetHashCode();
        }

        public Guid ToGuid()
        {
            return this.id;
        }

        public override string ToString()
        {
            return this.id.ToString();
        }

        public static bool operator ==(RpcServerId id1, RpcServerId id2)
        {
            return id1.id == id2.id;
        }

        public static bool operator !=(RpcServerId id1, RpcServerId id2)
        {
            return id1.id != id2.id;
        }
    }

    /// <summary>
    /// The RpcServerConnectionInfo class contains base information about a connected 
    /// server. This base class only provides the name and id of the server. Use
    /// derived classes to access protocol specific connection data.
    /// </summary>
    [DataContract]
    //[KnownType(typeof(TcpRpcServerConnectionInfo))]
    [Serializable]
    public class RpcServerConnectionInfo : IEquatable<RpcServerConnectionInfo>
    {
        /// <summary>
        /// Initializes a new ServerConnectionInfo with the supplied displayName and serverId.
        /// </summary>
        /// <param name="displayName">The display name of the server connection.</param>
        /// <param name="serverId">Id of the server.</param>
        public RpcServerConnectionInfo(string displayName, Uri? hostUrl, RpcServerId serverId=default)
        {
            this.DisplayName = displayName ?? hostUrl?.Host ?? "";
            this.HostUrl = hostUrl?.ToString() ?? "";
            this.ServerId = serverId;
        }

        /// <summary>
        /// Initializes a new ServerConnectionInfo with the supplied displayName and serverId.
        /// </summary>
        /// <param name="serverId">Id of the server.</param>
        public RpcServerConnectionInfo(Uri? hostUrl, RpcServerId serverId = default)
        {
            this.DisplayName = hostUrl?.Host ?? "";
            this.HostUrl = hostUrl?.ToString() ?? "";
            this.ServerId = serverId;
        }

        public RpcServerConnectionInfo(RpcServerId serverId )
        {
            this.DisplayName = "";
            this.HostUrl = "";
            this.ServerId = serverId;
        }


        /// <summary>
        /// Gets the display name of this server connection.
        /// </summary>
        [DataMember(Order = 1)]
        public string DisplayName
        {
            get;
            private set;
        }

        [DataMember(Order = 2)]
        public string HostUrl { get; private set; }

        /// <summary>
        /// Gets the id of the connected server.
        /// </summary>
        [DataMember(Order = 3)]
        public RpcServerId ServerId { get; private set; }

        public sealed override bool Equals(object obj)
        {
            return obj is RpcServerConnectionInfo other && this.Equals(other);
        }

        public virtual bool Equals(RpcServerConnectionInfo other)
        {
            if (other != null && this.GetType() == other.GetType())
            {
                return other.ServerId == this.ServerId;
            }

            return false;
        }

        public override int GetHashCode()
        {
            return this.ServerId.GetHashCode();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public virtual bool Matches(RpcServerConnectionInfo other)
        {
            if (other == null)
            {
                return false;
            }

            if (this.ServerId == RpcServerId.Empty || other.ServerId == RpcServerId.Empty)
            {
                // At least one connection info is missing the server id.
                // Let's just assume it's a match, since URLs may not necessarily match.
                return this.GetType().Equals(other.GetType());
            }

            return other.ServerId == this.ServerId;
        }

        /// <summary>
        /// Creates a new ServerConnectionInfo based on this one, but with the 
        /// ServerId initialized with the supplied id.
        /// </summary>
        /// <param name="serverId"></param>
        /// <returns></returns>
        public virtual RpcServerConnectionInfo SetServerId(RpcServerId serverId)
        {
            if (this.GetType() != typeof(RpcServerConnectionInfo))
            {
                throw new NotImplementedException("SetServerId must be implemented by derived class");
            }

            return new RpcServerConnectionInfo(this.DisplayName, !string.IsNullOrEmpty( this.HostUrl ) ? new Uri( this.HostUrl ) : null, serverId);
        }

        public override string ToString()
        {
            return this.DisplayName;
        }
    }
}
