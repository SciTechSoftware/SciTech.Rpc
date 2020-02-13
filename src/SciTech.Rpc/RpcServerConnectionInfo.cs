﻿#region Copyright notice and license
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
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SciTech.Rpc
{
    /// <summary>
    /// The RpcServerId struct is used to identify a logical RPC server. The logical RPC server may be a single server process, 
    /// or a set of load-balanced servers.
    /// </summary>
    [DataContract]
    [Serializable]
    [JsonConverter(typeof(Serialization.RpcServerIdJsonConverter))]
    public struct RpcServerId : IEquatable<RpcServerId>
    {
        public static readonly RpcServerId Empty;

        [DataMember(Order = 1)]
        internal Guid Id { get; set; }

        public RpcServerId(string idString)
        {
            this.Id = new Guid(idString);
        }

        public RpcServerId(Guid id)
        {
            this.Id = id;
        }

        public static RpcServerId NewId()
        {
            return new RpcServerId(Guid.NewGuid());
        }

        public override bool Equals(object? obj)
        {
            return obj is RpcServerId other && this.Equals(other);
        }

        public bool Equals(RpcServerId other)
        {
            return this.Id == other.Id;
        }

        public override int GetHashCode()
        {
            return this.Id.GetHashCode();
        }

        public Guid ToGuid()
        {
            return this.Id;
        }

        public override string ToString()
        {
            return this.Id.ToString();
        }

        public static bool operator ==(RpcServerId id1, RpcServerId id2)
        {
            return id1.Id == id2.Id;
        }

        public static bool operator !=(RpcServerId id1, RpcServerId id2)
        {
            return id1.Id != id2.Id;
        }
    }

    public class RpcServerConnectionInfoConverter : JsonConverter<RpcServerConnectionInfo>
    {
        public RpcServerConnectionInfoConverter()
        {
        }

        public override void Write(Utf8JsonWriter writer, RpcServerConnectionInfo value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("DisplayName", value.DisplayName);
            writer.WriteString("HostUrl", value.HostUrl?.ToString() ?? "");
            writer.WriteString("ServerId", value.ServerId.ToGuid());
            writer.WriteEndObject();
        }

        public override RpcServerConnectionInfo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException();
            }


            string displayName = "";
            string hostUrl = "";
            Guid serverId = Guid.Empty;
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        {
                            string propertyName = reader.GetString();
                            reader.Read();
                            switch (propertyName)
                            {
                                case "DisplayName":
                                    displayName = reader.GetString();
                                    break;
                                case "HostUrl":
                                    hostUrl = reader.GetString();
                                    break;
                                case "ServerId":
                                    serverId = reader.GetGuid();
                                    break;
                            }
                            reader.Skip();
                            break;
                        }
                    case JsonTokenType.EndObject:
                        return new RpcServerConnectionInfo(displayName, !string.IsNullOrEmpty(hostUrl) ? new Uri(hostUrl) : null, new RpcServerId(serverId));
                    default:
                        throw new JsonException();
                }
            }

            throw new JsonException();
        }
    }

    /// <summary>
    /// The RpcServerConnectionInfo class contains information about a connection 
    /// to an RPC server. 
    /// </summary>
    [DataContract]
    [Serializable]
    [JsonConverter(typeof(RpcServerConnectionInfoConverter))]
    public sealed class RpcServerConnectionInfo : IEquatable<RpcServerConnectionInfo>
    {
        [NonSerialized]
        private Uri? hostUrl;

        [DataMember(Name = "HostUrl", Order = 2)]
        private string? hostUrlString;

        public RpcServerConnectionInfo()
        {
            this.DisplayName = "";
            this.HostUrl = null;
        }

        public RpcServerConnectionInfo(RpcServerId serverId)
        {
            this.DisplayName = "";
            this.HostUrl = null;
            this.ServerId = serverId;
        }

        /// <summary>
        /// Initializes a new ServerConnectionInfo with the supplied displayName and serverId.
        /// </summary>
        /// <param name="serverId">Id of the server.</param>
        public RpcServerConnectionInfo(Uri? hostUrl, RpcServerId serverId = default)
        {
            this.DisplayName = hostUrl?.Host ?? "";
            this.HostUrl = hostUrl;
            this.ServerId = serverId;
        }

        /// <summary>
        /// Initializes a new ServerConnectionInfo with the supplied displayName and serverId.
        /// </summary>
        /// <param name="displayName">The display name of the server connection.</param>
        /// <param name="serverId">Id of the server.</param>
        public RpcServerConnectionInfo(string displayName, Uri? hostUrl, RpcServerId serverId = default)
        {
            this.DisplayName = displayName ?? hostUrl?.Host ?? "";
            this.HostUrl = hostUrl;
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

        public Uri? HostUrl
        {
            get
            {
                if (this.hostUrl == null && !string.IsNullOrEmpty(this.hostUrlString))
                {
                    if (!Uri.TryCreate(this.hostUrlString, UriKind.Absolute, out this.hostUrl))
                    {
                        this.hostUrlString = "";
                    }
                }

                return this.hostUrl;
            }
            private set
            {
                this.hostUrl = value;
                if (value != null)
                {
                    this.hostUrlString = value.ToString();
                }
                else
                {
                    this.hostUrlString = "";
                }
            }
        }

        /// <summary>
        /// Gets the id of the connected server.
        /// </summary>
        [DataMember(Order = 3)]
        public RpcServerId ServerId { get; private set; }

        public override sealed bool Equals(object? obj)
        {
            return obj is RpcServerConnectionInfo other && this.Equals(other);
        }

        public bool Equals(RpcServerConnectionInfo other)
        {
            // TODO: Equals should check all properties, but there are components
            // that currently assume that connection infos are equal as long as they refer to the 
            // same server id. Add a Matches method or similar instead.
            if (other != null )
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
        public bool Matches(RpcServerConnectionInfo other)
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
        public RpcServerConnectionInfo SetServerId(RpcServerId serverId)
        {
            if (this.GetType() != typeof(RpcServerConnectionInfo))
            {
                throw new NotImplementedException("SetServerId must be implemented by derived class");
            }

            return new RpcServerConnectionInfo(this.DisplayName, this.HostUrl, serverId);
        }

        public override string ToString()
        {
            return this.DisplayName;
        }
    }
}
