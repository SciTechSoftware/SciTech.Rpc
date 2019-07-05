using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace SciTech.Rpc.Client
{
    public class SslClientOptions
    {
        public SslClientOptions()
        {
#if !PLAT_SYSTEM_SSL_PROTOCOLS
            this.EnabledSslProtocols = SslProtocols.Tls11 | SslProtocols.Tls12;
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
