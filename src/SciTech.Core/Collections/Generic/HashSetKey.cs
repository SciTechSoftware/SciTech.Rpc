#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB.
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

namespace SciTech.Collections.Generic
{
    public struct HashSetKey<T> : IEquatable<HashSetKey<T>>
    {
        private readonly HashSet<T> hashSet;

        private readonly int hashCode;

        public HashSetKey(IEnumerable<T> items)
        {
            this.hashSet = new HashSet<T>(items);
            this.hashCode = 0;
            foreach (var item in this.hashSet)
            {
                this.hashCode += item.GetHashCode();
            }
        }

        public HashSetKey(HashSet<T> hashSet) : this()
        {
            this.hashSet = hashSet ?? throw new ArgumentNullException(nameof(hashSet));
            this.hashCode = 0;
            foreach (var item in hashSet)
            {
                this.hashCode += item.GetHashCode();
            }
        }

        public bool Equals(HashSetKey<T> other)
        {
            if (this.hashSet == null)
            {
                return other.hashSet == null;
            }
            else if (other.hashSet == null || this.hashSet.Count != other.hashSet.Count)
            {
                return false;
            }

            return this.hashSet.SetEquals(other.hashSet);
        }

        public override bool Equals(object obj)
        {
            return obj is HashSetKey<T> other && this.Equals(other);
        }

        public override int GetHashCode() => this.hashCode;

        public static bool operator ==(HashSetKey<T> left, HashSetKey<T> right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(HashSetKey<T> left, HashSetKey<T> right)
        {
            return !(left == right);
        }
    }
}
