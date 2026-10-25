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

using SciTech.Rpc.Serialization.Internal;
using System;
using System.Runtime.Serialization;
using System.Xml;

namespace SciTech.Rpc.Serialization
{
    /// <summary>
    /// Resolves the known types declared by <see cref="RpcKnownTypeAttribute"/>, using the discriminator as the type name.
    /// </summary>
    internal sealed class RpcKnownTypeContractResolver : DataContractResolver
    {
        internal const string KnownTypeNamespace = "urn:SciTech.Rpc:KnownTypes";

        private readonly DataContractResolver? innerResolver;

        internal RpcKnownTypeContractResolver(DataContractResolver? innerResolver)
        {
            this.innerResolver = innerResolver;
        }

        public override Type? ResolveName(string typeName, string? typeNamespace, Type? declaredType, DataContractResolver knownTypeResolver)
        {
            if (typeNamespace == KnownTypeNamespace && declaredType != null
                && RpcKnownTypes.FindKnownType(declaredType, typeName) is RpcKnownTypeAttribute knownType)
            {
                return knownType.KnownType;
            }

            return this.innerResolver != null
                ? this.innerResolver.ResolveName(typeName, typeNamespace, declaredType, knownTypeResolver)
                : knownTypeResolver.ResolveName(typeName, typeNamespace, declaredType, null!);
        }

        public override bool TryResolveType(Type type, Type? declaredType, DataContractResolver knownTypeResolver, out XmlDictionaryString? typeName, out XmlDictionaryString? typeNamespace)
        {
            bool resolved = this.innerResolver != null
                ? this.innerResolver.TryResolveType(type, declaredType, knownTypeResolver, out typeName, out typeNamespace)
                : knownTypeResolver.TryResolveType(type, declaredType, null!, out typeName, out typeNamespace);
            if (resolved)
            {
                return true;
            }

            if (declaredType != null && RpcKnownTypes.FindKnownType(declaredType, type) is RpcKnownTypeAttribute knownType)
            {
                var dictionary = new XmlDictionary(2);
                typeName = dictionary.Add(knownType.Discriminator);
                typeNamespace = dictionary.Add(KnownTypeNamespace);
                return true;
            }

            return false;
        }
    }
}
