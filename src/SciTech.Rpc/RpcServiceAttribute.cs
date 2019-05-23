﻿using System;
using System.Linq;

namespace SciTech.Rpc
{
    public enum RpcServiceDefinitionType
    {
        Both,
        Client,
        Server
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Event | AttributeTargets.Property)]
    public class RpcOperationAttribute : Attribute
    {
        public string Name { get; set; } = "";
    }

    [AttributeUsage(AttributeTargets.Interface)]
    public class RpcServiceAttribute : Attribute
    {
        private RpcServiceDefinitionType? serviceDefinitionType;

        /// <summary>
        /// Optional information about the server side definition type, used when <see cref="ServiceDefinitionType"/> is 
        /// <see cref="RpcServiceDefinitionType.Client"/>. Specifiying this type allows RPC analyzer and runtime code generation to validate 
        /// that the client definition matches the server definition. It also allows service name and similar properties to be retrieved
        /// from the server side definition.
        /// </summary>
        public Type? ServerDefinitionType { get; set; }

        /// <summary>
        /// Optional information about the type name of the server side definition type. This property can be used instead of <see cref="ServerDefinitionType"/> 
        /// when the server definition type is not available from the client definition assembly. 
        /// </summary>
        /// <remarks>
        /// If both <see cref="ServerDefinitionType"/>  and <see cref="ServerDefinitionTypeName"/> are specified, then <see cref="ServerDefinitionType"/> takes precendence.
        /// Specifiying this type name allows the RPC analyzer to validate that the client definition matches the server definition. This
        /// type name is not used by the runtime code generator.
        /// </remarks>
        public string ServerDefinitionTypeName { get; set; } = "";

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
        /// Indicates that this service will always be published as a singleton. It cannot be associated 
        /// with an object id.
        /// </summary>
        public bool IsSingleton { get; set; }

        /// <summary>
        /// TODO: Change suffix. Type is misleading since it's not a Type
        /// </summary>
        public RpcServiceDefinitionType ServiceDefinitionType
        {
            get => this.serviceDefinitionType ?? (this.ServerDefinitionType != null ? RpcServiceDefinitionType.Client : RpcServiceDefinitionType.Both);
            set => this.serviceDefinitionType = value;
        }
    }
}
