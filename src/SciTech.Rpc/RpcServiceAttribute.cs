﻿#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using System;
using System.Linq;

namespace SciTech.Rpc
{
    public enum RpcServiceDefinitionSide
    {
        Both,
        Client,
        Server
    }

    [AttributeUsage(AttributeTargets.Interface)]
    public class RpcServiceAttribute : Attribute
    {
        private RpcServiceDefinitionSide? serviceDefinitionType;

        /// <summary>
        /// Indicates whether faults are allowed in service operations  (e.g. if <see cref="RpcFaultException"/>s and <see cref="RpcFaultException{TFault}"/>
        /// are propagated to the caller. If <c>false</c> then fault exceptions will be converted to <see cref="RpcDefinitionException"/>s, which will
        /// not (by default) include any exception details. This option can be overridden by specific operations by using the 
        /// <see cref="RpcOperationAttribute.AllowFault">RpcOperationAttribute.AllowFault</see> property. The default value
        /// for this property is <c>true</c>.
        /// </summary>
        public bool AllowFault { get; set; } = true;

        /// <summary>
        /// Indicates that this service will always be published as a singleton. It cannot be associated 
        /// with an object id.
        /// </summary>
        public bool IsSingleton { get; set; }

        /// <summary>
        /// Gets the name of this RPC service. If not specified, the name will be retrieved from the <see cref="ServerDefinitionType"/> if available. Otherwise, 
        /// the name of the service interface with the initial 'I' removed will be used.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Gets the namespace of this service. This corresponds to the package name of a gRPC service. If not specified, the name 
        /// will be retrieved from the <see cref="ServerDefinitionType"/> if available. Otherwise, 
        /// the namespace of the service interface will be used.
        /// </summary>
        public string Namespace { get; set; } = "";

        /// <summary>
        /// Optional information about the server side definition type, used when <see cref="ServiceDefinitionSide"/> is 
        /// <see cref="RpcServiceDefinitionSide.Client"/>. Specifying this type allows RPC analyzer and runtime code generation to validate 
        /// that the client definition matches the server definition. It also allows service name and similar properties to be retrieved
        /// from the server side definition.
        /// </summary>
        public Type? ServerDefinitionType { get; set; }

        /// <summary>
        /// Optional information about the type name of the server side definition type. This property can be used instead of <see cref="ServerDefinitionType"/> 
        /// when the server definition type is not available from the client definition assembly. 
        /// </summary>
        /// <remarks>
        /// If both <see cref="ServerDefinitionType"/>  and <see cref="ServerDefinitionTypeName"/> are specified, then <see cref="ServerDefinitionType"/> takes precedence.
        /// Specifying this type name allows the RPC analyzer to validate that the client definition matches the server definition. This
        /// type name is not used by the runtime code generator.
        /// </remarks>
        public string ServerDefinitionTypeName { get; set; } = "";

        /// <summary>
        /// Indicates whether the service interface defines the server side, client side, or both sides of the RPC service. If <see cref="ServerDefinitionType"/>
        /// is specified, this property will be <see cref="RpcServiceDefinitionSide.Client"/> by default; otherwise it will be <see cref="RpcServiceDefinitionSide.Both"/>.
        /// </summary>
        public RpcServiceDefinitionSide ServiceDefinitionSide
        {
            get => this.serviceDefinitionType ?? (this.ServerDefinitionType != null ? RpcServiceDefinitionSide.Client : RpcServiceDefinitionSide.Both);
            set => this.serviceDefinitionType = value;
        }
    }
}
