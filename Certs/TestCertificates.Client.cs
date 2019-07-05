#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
//
// Adapted from grpc-net (ClientResources.cs):
//
// Copyright 2019 The gRPC Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#endregion

using SciTech.Rpc.Client;
using System;
using System.IO;
using GrpcCore=Grpc.Core;

namespace SciTech.Rpc
{
    public static partial class TestCertificates
    {
        public static readonly string ClientCertDir = AppContext.BaseDirectory;// Path.Combine(GetSolutionDirectory(), "examples", "Certs");

        public static readonly GrpcCore.SslCredentials GrpcSslCredentials
            = new GrpcCore.SslCredentials(
                File.ReadAllText(Path.Combine(ClientCertDir, "ca.crt")),
                new GrpcCore.KeyCertificatePair(
                    File.ReadAllText(Path.Combine(ClientCertDir, "client.crt")),
                    File.ReadAllText(Path.Combine(ClientCertDir, "client.key"))));

        public static readonly SslClientOptions SslClientOptions = new SslClientOptions
        {
            // Just for testing! Allow any server certificate
            RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors)=>true
        };
            
            
    }
}
