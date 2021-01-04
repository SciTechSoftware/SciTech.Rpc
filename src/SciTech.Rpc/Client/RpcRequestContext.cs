using SciTech.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace SciTech.Rpc.Client
{
    public class RpcRequestContext : IRpcRequestContext
    {
        private Dictionary<string, ImmutableArray<byte>>? headersDictionary;

        public CancellationToken CancellationToken { get; }

        public RpcRequestContext(CancellationToken cancellationToken)
        {
            this.CancellationToken = cancellationToken;
        }

        internal IReadOnlyDictionary<string, ImmutableArray<byte>>? Headers => this.headersDictionary;

        public void AddBinaryHeader(string key, IReadOnlyList<byte> value)
        {
            this.headersDictionary ??= new Dictionary<string, ImmutableArray<byte>>();
            this.headersDictionary.Add(key, value.ToImmutableArray());
        }

        public void AddHeader(string key, string value)
        {
            this.headersDictionary ??= new Dictionary<string, ImmutableArray<byte>>();

            this.headersDictionary.Add(key, ToHeaderBytes(value));
        }
        
        public static string? StringFromHeaderBytes(ImmutableArray<byte> value)
        {
            if( !value.IsDefaultOrEmpty)
            {
#if PLAT_SPAN_OVERLOADS
                return Encoding.UTF8.GetString(value.AsSpan());
#else
                return Encoding.UTF8.GetString(value.ToArray());
#endif
            }

            return null;
        }

        public static ImmutableArray<byte> ToHeaderBytes(string? value)
        {
            if (value != null )
            {
                return Encoding.UTF8.GetBytes(value).ToImmutableArray();
            }

            return default;
        }


        public string? GetHeaderString(string key)
        {
            if (this.headersDictionary != null && this.headersDictionary.TryGetValue(key, out var value))
            {
                return StringFromHeaderBytes(value);
            }

            return null;
        }

        public ImmutableArray<byte> GetBinaryHeader(string key)
        {
            if (this.headersDictionary != null && this.headersDictionary.TryGetValue(key, out var value))
            {
                return value;
            }

            return default; // or ImmutableArray<byte>.Empty?;
        }
    }
}
