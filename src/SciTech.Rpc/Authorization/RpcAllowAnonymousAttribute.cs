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
    /// <para>Specifies that the class, interface, method, property, or event that this attribute 
    /// is applied to does not require authorization.
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
    public sealed class RpcAllowAnonymousAttribute : Attribute
    {
        public RpcAllowAnonymousAttribute()
        {

        }
    }
}
