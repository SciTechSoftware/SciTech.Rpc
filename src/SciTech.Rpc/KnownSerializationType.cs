#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB and TA Instrument Inc.
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
    public class KnownSerializationType
    {
        public KnownSerializationType(Type knownType)
            : this(knownType, null, 0)
        {
        }

        public KnownSerializationType(Type knownType, int order)
            : this(knownType, null, order)
        {
        }

        public KnownSerializationType(Type knownType, string? discriminator)
            : this(knownType, discriminator, 0)
        {
        }

        public KnownSerializationType(Type knownType, string? discriminator, int order)
        {
            this.KnownType = knownType ?? throw new ArgumentNullException(nameof(knownType));
            this.Order = order;
            this.Discriminator = discriminator ?? knownType.Name;
        }


        public Type KnownType { get; }

        public int Order { get; }

        public string Discriminator {  get; }
    }
}
