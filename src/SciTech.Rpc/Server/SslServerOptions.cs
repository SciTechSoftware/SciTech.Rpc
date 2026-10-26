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
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace SciTech.Rpc.Server
{

    public class SslServerOptions : AuthenticationServerOptions
    {
        public SslServerOptions() : base( "ssl" )
        {
        }

        public SslServerOptions(X509Certificate serverCertificate) : this()
        {
            this.ServerCertificate = serverCertificate;
        }

        public X509RevocationMode CertificateRevocationCheckMode { get; set; }

        public bool ClientCertificateRequired { get; set; }

        public SslProtocols EnabledSslProtocols { get; set; }

        public EncryptionPolicy EncryptionPolicy { get; set; }

        public RemoteCertificateValidationCallback? RemoteCertificateValidationCallback { get; set; }

        public X509Certificate? ServerCertificate { get; set; }
    }
}
