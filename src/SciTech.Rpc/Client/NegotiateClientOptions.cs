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
