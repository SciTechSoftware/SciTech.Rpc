using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SciTech.Rpc.Lightweight.Internal
{
    /// <summary>
    /// Operations, limits and helpers of the discovery protocol, see the Lightweight protocol specification
    /// (docs/lightweight-protocol.md, section 7).
    /// </summary>
    internal static class ServiceDiscoveryOperations
    {
        /// <summary>
        /// Request: <see cref="RpcFindServersRequest"/>
        /// Response: <see cref="RpcFindServersResponse"/>
        /// </summary>
        internal const string FindServers = "SciTech.Rpc.Discovery.FindServers";

        /// <summary>
        /// Request: <see cref="RpcGetServicesRequest"/>
        /// Response: <see cref="RpcGetServicesResponse"/>
        /// </summary>
        internal const string GetServices = "SciTech.Rpc.Discovery.GetServices";

        /// <summary>
        /// The minimum size of a discovery request datagram. Requests are padded with zero bytes after the frame to
        /// reach it, so that a spoofed request can't make the server send much more data than it received.
        /// </summary>
        internal const int MinRequestSize = 512;

        /// <summary>
        /// The maximum size of a discovery datagram, the largest UDP payload that won't be fragmented on IPv6
        /// networks.
        /// </summary>
        internal const int MaxDatagramSize = 1200;

        /// <summary>
        /// The maximum number of requested services that a server considers in a <see cref="FindServers"/> request.
        /// </summary>
        internal const int MaxRequestedServices = 32;

        /// <summary>
        /// The maximum size, in UTF-8 bytes, of a discoverable service name.
        /// </summary>
        internal const int MaxServiceNameSize = 256;

        /// <summary>
        /// The maximum size, in UTF-8 bytes, of the display name of a discovery end point.
        /// </summary>
        internal const int MaxDisplayNameSize = 128;

        /// <summary>
        /// The maximum size, in UTF-8 bytes, of the host URL of a discovery end point.
        /// </summary>
        internal const int MaxHostUrlSize = 256;

        /// <summary>
        /// The serializer of the discovery payloads. Non-ASCII characters are not escaped, to keep the size limits
        /// meaningful.
        /// </summary>
        internal static readonly IRpcSerializer DiscoverySerializer = new JsonRpcSerializer(new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        /// <summary>
        /// Pads <paramref name="frameData"/> with zero bytes to <see cref="MinRequestSize"/>, if it's shorter.
        /// </summary>
        internal static byte[] PadRequest(byte[] frameData)
        {
            if (frameData.Length >= MinRequestSize)
            {
                return frameData;
            }

            var paddedData = new byte[MinRequestSize];
            Buffer.BlockCopy(frameData, 0, paddedData, 0, frameData.Length);
            return paddedData;
        }

        /// <summary>
        /// Computes the services hash of <paramref name="sortedServiceNames"/>, the UTF-8 names of the discoverable
        /// services sorted by their bytes: the 64-bit FNV-1a hash of the names, each followed by a 0x0A byte, as 16
        /// lowercase hexadecimal digits.
        /// </summary>
        internal static string ComputeServicesHash(IEnumerable<byte[]> sortedServiceNames)
        {
            const ulong OffsetBasis = 14695981039346656037;
            const ulong Prime = 1099511628211;

            ulong hash = OffsetBasis;
            foreach (var name in sortedServiceNames)
            {
                foreach (var b in name)
                {
                    hash = unchecked((hash ^ b) * Prime);
                }

                hash = unchecked((hash ^ 0x0A) * Prime);
            }

            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Compares UTF-8 names by their bytes.
        /// </summary>
        internal static int CompareUtf8(byte[] x, byte[] y)
        {
            int length = Math.Min(x.Length, y.Length);
            for (int i = 0; i < length; i++)
            {
                int diff = x[i].CompareTo(y[i]);
                if (diff != 0) return diff;
            }

            return x.Length.CompareTo(y.Length);
        }

        internal static int GetUtf8Size(string? text) => text != null ? Encoding.UTF8.GetByteCount(text) : 0;
    }
}
