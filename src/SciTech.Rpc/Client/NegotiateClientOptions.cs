using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace SciTech.Rpc.Client
{
    public class NegotiateClientOptions : AuthenticationClientOptions
    {
        public NegotiateClientOptions() : base( "negotiate")
        {

        }

        public NetworkCredential? Credential { get; set; }

        public string? TargetName { get; set; }
    }

    public class AnonymousAuthenticationClientOptions: AuthenticationClientOptions
    {
        public static AnonymousAuthenticationClientOptions Instance { get; } = new AnonymousAuthenticationClientOptions();


        public AnonymousAuthenticationClientOptions() : base("anonymous")
        {
        }
    }
}
