using System;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace SciTech.Rpc.Server
{
    public class SslServerOptions
    {
        public SslServerOptions()
        {
#if !PLAT_SYSTEM_SSL_PROTOCOLS
#pragma warning disable CA5397 // Transport Layer Security protocol version 'Tls11' is deprecated.  Use 'None' to let the Operating System choose a version.
            this.EnabledSslProtocols = SslProtocols.Tls11 | SslProtocols.Tls12;
#pragma warning restore CA5397 // Transport Layer Security protocol version 'Tls11' is deprecated.  Use 'None' to let the Operating System choose a version.
#endif
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
