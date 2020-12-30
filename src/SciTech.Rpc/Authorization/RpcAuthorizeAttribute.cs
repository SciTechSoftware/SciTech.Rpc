#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using System;

namespace SciTech.Rpc.Authorization
{
    /// <summary>
    /// <para>
    /// Specifies that the class, interface, method, property, or event, that this attribute is applied to 
    /// requires the specified authorization.
    /// </para>
    /// <note type="caution">
    /// Authorization attributes are currently only implemented for the <c>NetGrpc</c>
    /// implementation of <c>SciTech.Rpc</c>. The attributes will be ignored by other
    /// implementations.
    /// </note>
    /// </summary>
    /// <remarks>
    /// When this attribute is applied to an RPC interface or interface member, it will apply to 
    /// all published RPC operations that implement the member.
    /// </remarks>
    [AttributeUsage(
        AttributeTargets.Class
        | AttributeTargets.Method
        | AttributeTargets.Interface
        | AttributeTargets.Property
        | AttributeTargets.Event)]
    public sealed class RpcAuthorizeAttribute : Attribute
    {
        public RpcAuthorizeAttribute()
        {

        }

        /// <summary>
        /// Initializes a new instance of the AuthorizeAttribute class with the specified policy.
        /// </summary>
        /// <param name="policy">The name of the policy to require for authorization.</param>
        public RpcAuthorizeAttribute(string policy)
        {
            this.Policy = policy;
        }

        /// <summary>
        /// Gets or sets a comma delimited list of schemes from which user information is constructed.
        /// </summary>
        public string? AuthenticationSchemes { get; set; }

        /// <summary>
        /// Gets or sets the policy name that determines access to the resource.
        /// </summary>
        public string? Policy { get; }

        /// <summary>
        /// Gets or sets a comma delimited list of roles that are allowed to access the resource.
        /// </summary>
        public string? Roles { get; set; }
    }
}
