using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace SciTech.Rpc.Client
{
    public class NegotiateClientOptions
    {
        public NetworkCredential? Credential { get; set; }

        public string? TargetName { get; set; }
    }
}
