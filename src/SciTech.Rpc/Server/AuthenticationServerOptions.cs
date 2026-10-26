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

namespace SciTech.Rpc.Server
{
    public class AuthenticationServerOptions
    {
        public string Name { get; }


        protected AuthenticationServerOptions(string name)
        {
            this.Name = name;
        }
    }

    public class AnonymousAuthenticationServerOptions : AuthenticationServerOptions
    {
        public static AnonymousAuthenticationServerOptions Instance { get; } = new AnonymousAuthenticationServerOptions();

        public AnonymousAuthenticationServerOptions() : base("anonymous")
        {
        }
    }
}
