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
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace SciTech.Rpc.Client
{

    public class SslClientOptions : AuthenticationClientOptions
    {
        public SslClientOptions() : base( "ssl" )
        {
        }

        public SslClientOptions(X509Certificate2 clientCertificate ) : this()
        {
            this.ClientCertificates.Add(clientCertificate);
        }

        public X509CertificateCollection ClientCertificates { get; } = new X509CertificateCollection();

        public X509RevocationMode CertificateRevocationCheckMode { get; set; }

        public SslProtocols EnabledSslProtocols { get; set; } 

        public LocalCertificateSelectionCallback? LocalCertificateSelectionCallback { get; set; }

        public RemoteCertificateValidationCallback? RemoteCertificateValidationCallback { get; set; }
        
        public EncryptionPolicy EncryptionPolicy { get; set; }
    }

}
