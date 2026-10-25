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

namespace SciTech.Rpc
{
    /// <summary>
    /// Declares a type derived from the attributed type that may be used wherever the attributed type is used in
    /// RPC messages, e.g. as an operation parameter, return value or data member.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The built-in serializers use this attribute to serialize the derived type polymorphically, without any
    /// serializer-specific attributes or registrations: <c>ProtobufRpcSerializer</c> adds the derived type as a
    /// sub-type with the field number <see cref="Order"/>, <c>JsonRpcSerializer</c> adds it as a derived type
    /// with the type discriminator <see cref="Discriminator"/>, and <c>DataContractRpcSerializer</c> resolves it
    /// by <see cref="Discriminator"/>.
    /// </para>
    /// <para>
    /// <see cref="Order"/> and <see cref="Discriminator"/> are part of the wire format, so they must not be
    /// changed once the type has been used in communication.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code lang="C#">
    /// [DataContract]
    /// [RpcKnownType(typeof(Circle), 10)]
    /// public class Shape { ... }
    ///
    /// [DataContract]
    /// public class Circle : Shape { ... }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class RpcKnownTypeAttribute : Attribute
    {
        private string? discriminator;

        /// <summary>
        /// Initializes a new instance of the <see cref="RpcKnownTypeAttribute"/> class.
        /// </summary>
        /// <param name="knownType">The derived type.</param>
        /// <param name="order">A positive number that identifies the derived type, unique among the known types of
        /// the attributed type. Used as the field number by the Protobuf serializer, so it must not collide with
        /// the member orders of the attributed type.</param>
        public RpcKnownTypeAttribute(Type knownType, int order)
        {
            if (order <= 0) throw new ArgumentOutOfRangeException(nameof(order), "The order of a known type must be positive.");

            this.KnownType = knownType ?? throw new ArgumentNullException(nameof(knownType));
            this.Order = order;
        }

        /// <summary>
        /// Gets the derived type.
        /// </summary>
        public Type KnownType { get; }

        /// <summary>
        /// Gets the number that identifies the derived type.
        /// </summary>
        public int Order { get; }

        /// <summary>
        /// Gets or sets the name that identifies the derived type, unique among the known types of the attributed type.
        /// The default is the name of <see cref="KnownType"/>.
        /// </summary>
        public string Discriminator
        {
            get => this.discriminator ?? this.KnownType.Name;
            set => this.discriminator = value;
        }
    }
}
