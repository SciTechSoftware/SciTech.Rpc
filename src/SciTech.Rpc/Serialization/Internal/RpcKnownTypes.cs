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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SciTech.Rpc.Serialization.Internal
{
    /// <summary>
    /// Provides the known types declared using <see cref="RpcKnownTypeAttribute"/>. Intended for serializer implementations.
    /// </summary>
    public static class RpcKnownTypes
    {
        private static readonly ConcurrentDictionary<Type, IReadOnlyList<RpcKnownTypeAttribute>> declaredKnownTypes = new();

        /// <summary>
        /// Gets the known types declared directly on <paramref name="type"/>, ordered by <see cref="RpcKnownTypeAttribute.Order"/>.
        /// </summary>
        /// <exception cref="RpcDefinitionException">The known types are not correctly declared.</exception>
        public static IReadOnlyList<RpcKnownTypeAttribute> GetDeclaredKnownTypes(Type type)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));

            return declaredKnownTypes.GetOrAdd(type, CreateDeclaredKnownTypes);
        }

        /// <summary>
        /// Finds the known type declaration of <paramref name="knownType"/> on <paramref name="declaredType"/> or one of its base types.
        /// </summary>
        public static RpcKnownTypeAttribute? FindKnownType(Type declaredType, Type knownType)
        {
            if (declaredType is null) throw new ArgumentNullException(nameof(declaredType));
            if (knownType is null) throw new ArgumentNullException(nameof(knownType));

            for (var type = declaredType; type != null && type != typeof(object); type = type.BaseType)
            {
                foreach (var attribute in GetDeclaredKnownTypes(type))
                {
                    if (attribute.KnownType == knownType && declaredType.IsAssignableFrom(knownType))
                    {
                        return attribute;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the known type with the discriminator <paramref name="discriminator"/>, declared on <paramref name="declaredType"/>
        /// or one of its base types.
        /// </summary>
        public static RpcKnownTypeAttribute? FindKnownType(Type declaredType, string discriminator)
        {
            if (declaredType is null) throw new ArgumentNullException(nameof(declaredType));

            for (var type = declaredType; type != null && type != typeof(object); type = type.BaseType)
            {
                foreach (var attribute in GetDeclaredKnownTypes(type))
                {
                    if (attribute.Discriminator == discriminator && declaredType.IsAssignableFrom(attribute.KnownType))
                    {
                        return attribute;
                    }
                }
            }

            return null;
        }

        private static IReadOnlyList<RpcKnownTypeAttribute> CreateDeclaredKnownTypes(Type type)
        {
            var attributes = type.GetCustomAttributes<RpcKnownTypeAttribute>(false).OrderBy(a => a.Order).ToArray();
            if (attributes.Length == 0)
            {
                return Array.Empty<RpcKnownTypeAttribute>();
            }

            var orders = new HashSet<int>();
            var discriminators = new HashSet<string>();
            foreach (var attribute in attributes)
            {
                if (!type.IsAssignableFrom(attribute.KnownType) || attribute.KnownType == type)
                {
                    throw new RpcDefinitionException($"Known type '{attribute.KnownType}' must be derived from '{type}'.");
                }

                if (!orders.Add(attribute.Order))
                {
                    throw new RpcDefinitionException($"Known type order {attribute.Order} is declared more than once on '{type}'.");
                }

                if (!discriminators.Add(attribute.Discriminator))
                {
                    throw new RpcDefinitionException($"Known type discriminator '{attribute.Discriminator}' is declared more than once on '{type}'.");
                }
            }

            return attributes;
        }
    }
}
