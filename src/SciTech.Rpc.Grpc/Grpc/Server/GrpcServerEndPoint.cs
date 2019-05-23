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


using SciTech.Rpc.Server;
using System;
using System.IO;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.Grpc.Server
{
    public class GrpcServerEndPoint : IRpcServerEndPoint
    {
        public const string GrpcScheme = "grpc";

        public GrpcServerEndPoint(string hostName, int port, bool bindToAllInterfaces, GrpcCore.ServerCredentials? credentials = null)
            : this(hostName, hostName, port, bindToAllInterfaces, credentials)
        {
        }

        public GrpcServerEndPoint(string displayName, string hostName, int port, bool bindToAllInterfaces, GrpcCore.ServerCredentials? credentials = null)
        {
            if (string.IsNullOrWhiteSpace(hostName))
            {
                throw new ArgumentException("Host name must be provided", nameof(hostName));
            }

            this.DisplayName = !string.IsNullOrWhiteSpace(displayName) ? displayName : hostName;
            this.HostName = hostName;
            this.Port = port;
            this.BindToAllInterfaces = bindToAllInterfaces;
            this.Credentials = credentials ?? GrpcCore.ServerCredentials.Insecure;
        }

        public bool BindToAllInterfaces { get; }

        public GrpcCore.ServerCredentials Credentials { get; }

        public string DisplayName { get; }

        public string HostName { get; }

        public int Port { get; }

        public RpcServerConnectionInfo GetConnectionInfo(RpcServerId hostId)
        {
            return new RpcServerConnectionInfo(this.DisplayName, new Uri( $"{GrpcScheme}://{this.HostName}:{this.Port}" ), hostId);
        }

        internal GrpcCore.ServerPort CreateServerPort()
        {
            return new GrpcCore.ServerPort(this.BindToAllInterfaces ? "0.0.0.0" : this.HostName, this.Port, this.Credentials);
        }
    }
}
