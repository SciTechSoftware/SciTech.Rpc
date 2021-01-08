using SciTech.Rpc.Server;
using System.Net;

namespace SciTech.Rpc.Lightweight.Server
{
    public class NegotiateServerOptions : AuthenticationServerOptions
    {
        public NegotiateServerOptions() :  base( "negotiate")
        {

        }

        public NetworkCredential? Credential { get; set; }
    }
}
