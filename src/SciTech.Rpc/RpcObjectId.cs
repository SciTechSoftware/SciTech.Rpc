using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace SciTech.Rpc
{
#pragma warning disable CA2235 // Mark all non-serializable fields
    [DataContract]
    [Serializable]  
    public struct RpcObjectId : IEquatable<RpcObjectId>
    {
        public static readonly RpcObjectId Empty = default;

#pragma warning disable IDE0044 // Add readonly modifier
        [DataMember(Order = 1,Name ="Id")]
        private Guid id;
#pragma warning restore IDE0044 // Add readonly modifier

        private RpcObjectId(Guid id)
        {
            this.id = id;
        }

        public RpcObjectId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("id should not be empty.", nameof(id));
            }

            this.id = new Guid(id);
        }

        public override string ToString()
        {
            return this.id.ToString();
        }

        public static RpcObjectId NewId()
        {
            return new RpcObjectId(Guid.NewGuid());
        }

        public override bool Equals(object? obj)
        {
            return obj is RpcObjectId other && this.Equals(other);
        }

        public static bool operator ==(RpcObjectId first, RpcObjectId second)
        {
            return first.Equals(second);
        }

        public static bool operator !=(RpcObjectId first, RpcObjectId second)
        {
            return !(first == second);
        }

        public bool Equals(RpcObjectId other)
        {
            return this.id.Equals(other.id);
        }

        public override int GetHashCode()
        {
            return this.id.GetHashCode();
        }
    }
#pragma warning restore CA2235 // Mark all non-serializable fields
}
