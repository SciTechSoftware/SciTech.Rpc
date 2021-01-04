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
        public SslClientOptions()
        {
#if !PLAT_SYSTEM_SSL_PROTOCOLS
#pragma warning disable CA5397 // Transport Layer Security protocol version 'Tls11' is deprecated.  Use 'None' to let the Operating System choose a version.
            this.EnabledSslProtocols = SslProtocols.Tls11 | SslProtocols.Tls12;
#pragma warning restore CA5397 // Transport Layer Security protocol version 'Tls11' is deprecated.  Use 'None' to let the Operating System choose a version.
#endif
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
