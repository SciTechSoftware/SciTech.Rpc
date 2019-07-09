#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using SciTech.Buffers;
using SciTech.Rpc.Lightweight.IO;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SciTech.Rpc.Lightweight.Internal
{

    /// <summary>
    /// short RPCVersion: Currently always 1
    /// short RpcFrameType: Frame type (from the <see cref="RpcFrameType"/> enum.
    /// int32 FrameLength: Total length of frame, including header
    /// int32 MessageNumber: Message identifier
    /// int32 PayloadLength: Total length of serialized payload
    /// String Operation: Defines the RPC operation associated with the frame. May be empty if not needed.
    /// varint OperationFlags: Operation flags (from the <see cref="RpcOperationFlags"/> enum.
    /// varint TimeOut: Operation timeout, in milliseconds (0 means no timeout)
    /// varint HeaderPairsCount: Number of header pairs (following this field)
    /// StringPair[] HeaderPairs: Array of header pairs. Length specified by HeaderPairsCount
    ///     String Key: UTF8 encoded header key
    ///     String Value: UTF8 encoded header value
    ///  byte[] Payload: Frame payload, e.g. request or response data. Size specified by PayloadLength.
    ///  
    /// 
    /// short and int32: encoded as little-endian integers
    /// varint: Protobuf style encoded integer
    /// String: Protobuf style encoded UTF8 string
    /// </summary>
    internal readonly struct LightweightRpcFrame
    {
        public const int DefaultMaxFrameLength = 65536;

        public const int MinimumHeaderLength = 16;

        private const short CurrentVersion = 1;

        public LightweightRpcFrame(
            RpcFrameType frameType, int messageNumber, string rpcOperation,
            IReadOnlyCollection<KeyValuePair<string, string>>? headers)
        {
            this.FrameType = frameType;
            this.RpcOperation = rpcOperation;
            this.MessageNumber = messageNumber;
            this.Payload = ReadOnlySequence<byte>.Empty;
            this.Headers = headers;

            this.OperationFlags = 0;
            this.Timeout = 0;
        }

        public LightweightRpcFrame(RpcFrameType frameType, int messageNumber, string rpcOperation, RpcOperationFlags flags, uint timeout, IReadOnlyCollection<KeyValuePair<string, string>>? headers)
        {
            this.FrameType = frameType;
            this.RpcOperation = rpcOperation;
            this.MessageNumber = messageNumber;
            this.OperationFlags = flags;
            this.Timeout = timeout;
            this.Payload = ReadOnlySequence<byte>.Empty;
            this.Headers = headers;
        }

        public LightweightRpcFrame(
            RpcFrameType frameType, int messageNumber, string rpcOperation, RpcOperationFlags flags, uint timeout,
            in ReadOnlySequence<byte> payload, IReadOnlyCollection<KeyValuePair<string, string>> headers)
        {
            this.FrameType = frameType;
            this.RpcOperation = rpcOperation;
            this.MessageNumber = messageNumber;
            this.Payload = payload;
            this.Headers = headers;

            this.OperationFlags = flags;
            this.Timeout = timeout;

        }

        public RpcFrameType FrameType { get; }

        public IReadOnlyCollection<KeyValuePair<string, string>>? Headers { get; }

        public int MessageNumber { get; }

        public RpcOperationFlags OperationFlags { get; }

        /// <summary>
        /// The payload data following the header. May be empty when building a request or response.
        /// </summary>
        public ReadOnlySequence<byte> Payload { get; }

        public string RpcOperation { get; }

        public uint Timeout { get; }

        /// <summary>
        /// Tries to read an RPC frame
        /// </summary>
        /// <param name="input"></param>
        /// <param name="maxFrameLength"></param>
        /// <param name="message"></param>
        /// <returns><c>true</c> if data for the full frame is available in <paramref name="input"/>, <c>false</c> if 
        /// not enough data is available.</returns>
        /// <exception cref="InvalidDataException">Thrown if the frame contains invalid data (not including the payload), or 
        /// if the frame is too big.</exception>
        public static bool TryRead(ref ReadOnlySequence<byte> input, int maxFrameLength, out LightweightRpcFrame message)
        {
            message = default;
            if (input.Length < MinimumHeaderLength)
            {
                return false;
            }

            var currInput = input;
            short version = ReadInt16LittleEndian(ref currInput);
            if (!IsValidVersion(version))
            {
                throw new InvalidDataException($"Invalid RPC version {version}.");
            }

            var frameType = (RpcFrameType)ReadInt16LittleEndian(ref currInput);
            int frameLength = ReadInt32LittleEndian(ref currInput);
            if (frameLength < 0 || frameLength >= maxFrameLength)
            {
                throw new InvalidDataException($"Invalid RPC frame length: {frameLength}");
            }

            // Check the frame length against the available input.
            // Should really be input (and not currInput), since frameLength includes the header and payload.
            if (frameLength > input.Length)
            {
                return false;
            }

            var messageNumber = ReadInt32LittleEndian(ref currInput);
            var payloadLength = ReadInt32LittleEndian(ref currInput);

            var operation = ReadString(ref currInput, frameLength);

            var operationFlags = (RpcOperationFlags)ReadUInt32Varint(ref currInput);
            var timeout = ReadUInt32Varint(ref currInput);

            uint headerPairsCount = ReadUInt32Varint(ref currInput);
            if (headerPairsCount * 2 > frameLength)
            {
                // Clearly not correct. An empty string is one byte, so we have more pairs than 
                // could possibly fit in the frame.
                throw new InvalidDataException();
            }

            ImmutableArray<KeyValuePair<string, string>> headers;

            if (headerPairsCount > 0)
            {
                var pairsBuilder = ImmutableArray.CreateBuilder<KeyValuePair<string, string>>((int)headerPairsCount);
                for (int pairIndex = 0; pairIndex < headerPairsCount; pairIndex++)
                {
                    string key = ReadString(ref currInput, frameLength);
                    string value = ReadString(ref currInput, frameLength);
                    pairsBuilder.Add(new KeyValuePair<string, string>(key, value));
                }

                headers = pairsBuilder.MoveToImmutable();
            }
            else
            {
                headers = ImmutableArray<KeyValuePair<string, string>>.Empty;
            }


            var payload = currInput.Slice(0, payloadLength);
            input = currInput.Slice(payloadLength);
            message = new LightweightRpcFrame(frameType, messageNumber, operation, operationFlags, timeout, payload, headers);
            return true;
        }

        internal static void EndWrite(int frameLength, in WriteState state)
        {
            int payloadLength = frameLength - state.HeaderLength;
            BinaryPrimitives.WriteInt32LittleEndian(state.FrameLengthSpan.Span, frameLength);
            BinaryPrimitives.WriteInt32LittleEndian(state.PayloadLengthSpan.Span, payloadLength);
        }

        internal WriteState BeginWrite(BufferWriterStream writer)
        {
            var memory = writer.GetMemory(MinimumHeaderLength);
            var span = memory.Span;
            BinaryPrimitives.WriteInt16LittleEndian(span, CurrentVersion);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(2), (short)this.FrameType);

            var frameLengthSpan = memory.Slice(4, 4);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(8), this.MessageNumber);
            var payloadLengthSpan = memory.Slice(12, 4);
            writer.Advance(MinimumHeaderLength);

            int headerLength = MinimumHeaderLength;
            headerLength += WriteString(writer, this.RpcOperation);

            headerLength += WriteUInt32Varint(writer, (uint)this.OperationFlags);
            headerLength += WriteUInt32Varint(writer, this.Timeout);

            var headers = this.Headers;
            int nHeaders = headers?.Count ?? 0;
            headerLength += WriteUInt32Varint(writer, (uint)nHeaders);
            if (nHeaders > 0)
            {
                foreach (var pair in headers!)
                {
                    headerLength += WriteString(writer, pair.Key);
                    headerLength += WriteString(writer, pair.Value);
                }
            }

            return new WriteState(headerLength, payloadLengthSpan, frameLengthSpan);
        }

        private static bool IsValidVersion(short version) => version == CurrentVersion;

        private static short ReadInt16LittleEndian(ref ReadOnlySequence<byte> input)
        {
            short value;
            if (input.First.Length >= 2)
            {
                value = BinaryPrimitives.ReadInt16LittleEndian(input.First.Span);
            }
            else
            {   // copy 2 bytes into a local span
                Span<byte> local = stackalloc byte[2];

                input.Slice(0, 2).CopyTo(local);
                value = BinaryPrimitives.ReadInt16LittleEndian(local);
            }

            input = input.Slice(2);
            return value;
        }

        private static int ReadInt32LittleEndian(ref ReadOnlySequence<byte> input)
        {
            int value;
            if (input.First.Length >= 4)
            {
                value = BinaryPrimitives.ReadInt32LittleEndian(input.First.Span);
            }
            else
            {   // copy 4 bytes into a local span
                // Temporarily changed to new due to https://github.com/dotnet/roslyn/issues/35764
                Span<byte> local = new byte[4];
                // Span<byte> local = stackalloc byte[4];

                input.Slice(0, 4).CopyTo(local);
                value = BinaryPrimitives.ReadInt32LittleEndian(local);
            }

            input = input.Slice(4);
            return value;
        }

        private static long ReadInt64LittleEndian(ref ReadOnlySequence<byte> input)
        {
            long value;
            if (input.First.Length >= 8)
            {
                value = BinaryPrimitives.ReadInt64LittleEndian(input.First.Span);
            }
            else
            {
                Span<byte> local = stackalloc byte[8];

                input.Slice(0, 8).CopyTo(local);
                value = BinaryPrimitives.ReadInt64LittleEndian(local);
            }

            input = input.Slice(8);
            return value;
        }

        private static string ReadString(ref ReadOnlySequence<byte> input, int maxLength)
        {
            uint length = ReadUInt32Varint(ref input);
            if (length > maxLength)
            {
                throw new InvalidDataException();
            }

            string value;
            if (input.First.Length >= length)
            {
                var memory = input.First.Slice(0, (int)length);
#if PLAT_SPAN_OVERLOADS
                value = Encoding.UTF8.GetString(memory.Span);
#else
                using (var ownedArray = OwnedArraySegment.Create<byte>(memory))
                {
                    value = Encoding.UTF8.GetString(ownedArray.Array, ownedArray.Offset, ownedArray.Count);
                }
#endif
            }
#if PLAT_SPAN_OVERLOADS
            else if (length <= 256)
            {
                Span<byte> local = stackalloc byte[(int)length];
                input.Slice(0, length).CopyTo(local);
                value = Encoding.UTF8.GetString(local);
            }
#endif
            else
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent((int)length);
                try
                {
                    input.Slice(0, length).CopyTo(buffer);
                    value = Encoding.UTF8.GetString(buffer, 0, (int)length);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            input = input.Slice(length);
            return value;
        }

        private static int ReadString(ReadOnlyMemory<byte> data, int maxLength, out string value)
        {
            int readPos = ReadUInt32Varint(data.Span, out uint length);
            if (length > maxLength)
            {
                throw new InvalidDataException();
            }

            var stringData = data.Slice(readPos, (int)length);
#if PLAT_SPAN_OVERLOADS
            value = Encoding.UTF8.GetString(stringData.Span);
#else
            using (var ownedArray = OwnedArraySegment.Create<byte>(stringData))
            {
                value = Encoding.UTF8.GetString(ownedArray.Array, ownedArray.Offset, ownedArray.Count);
            }
#endif
            return readPos + (int)length;
        }

        private static uint ReadUInt32Varint(ref ReadOnlySequence<byte> input)
        {
            int length;
            uint value;

            if (input.First.Length >= 5)
            {
                length = ReadUInt32Varint(input.First.Span, out value);
            }
            else
            {
                // copy up to 5 bytes into a local span
                int nBytes = (int)Math.Min(5, input.Length);

                Span<byte> copiedData = stackalloc byte[nBytes];
                input.Slice(0, nBytes).CopyTo(copiedData);

                length = ReadUInt32Varint(copiedData, out value);
            }
            input = input.Slice(length);
            return value;
        }

        private static int ReadUInt32Varint(ReadOnlySpan<byte> data, out uint value)
        {
            int readPos = 0;
            value = data[readPos++];
            if ((value & 0x80) == 0)
            {
                return 1;
            }

            value &= 0x7F;

            uint chunk = data[readPos++];
            value |= (chunk & 0x7F) << 7;
            if ((chunk & 0x80) == 0)
            {
                return 2;
            }

            chunk = data[readPos++];
            value |= (chunk & 0x7F) << 14;
            if ((chunk & 0x80) == 0)
            {
                return 3;
            }

            chunk = data[readPos++];
            value |= (chunk & 0x7F) << 21;
            if ((chunk & 0x80) == 0)
            {
                return 4;
            }

            chunk = data[readPos];
            value |= chunk << 28; // can only use 4 bits from this chunk
            if ((chunk & 0xF0) == 0)
            {
                return 5;
            }

            throw new InvalidDataException();
        }

        private static void ThrowNoEquals()
        {
            throw new NotSupportedException($"{nameof(LightweightRpcFrame)} should not be tested for equality.");
        }
        //public struct ByteStringKey : IEquatable<ByteStringKey>
        //{
        //    private Memory<byte> memoryData;
        //    private ImmutableArray<byte> arrayData;
        //    public bool Equals(ByteStringKey other)
        //    {
        //        throw new NotImplementedException();
        //    }
        //}
        //private Dictionary<ByteStringKey, WeakReference<string>> stringDictionary = new Dictionary<ByteStringKey, WeakReference<string>>();

        private static int WriteString(IBufferWriter<byte> writer, string value)
        {
            var encoding = Encoding.UTF8;
#if PLAT_SPAN_OVERLOADS
            ReadOnlySpan<char> stringSpan = value;
            int nEncodedBytes = encoding.GetByteCount(stringSpan);
            int nTotalBytes = WriteUInt32Varint(writer, (uint)nEncodedBytes);
            nTotalBytes += nEncodedBytes;


            var destSpan = writer.GetSpan(nEncodedBytes);
            int nRetrievedBytes = encoding.GetBytes(stringSpan, destSpan);
            Debug.Assert(nRetrievedBytes == nEncodedBytes);
            writer.Advance(nEncodedBytes);
#else
            int nEncodedBytes = encoding.GetByteCount(value);
            int nTotalBytes = WriteUInt32Varint(writer, (uint)nEncodedBytes);
            nTotalBytes += nEncodedBytes;
            var dest = writer.GetMemory(nEncodedBytes);

            using (var ownedArray = OwnedArraySegment.Create<byte>(dest))
            {
                int nRetrivedBytes = Encoding.UTF8.GetBytes(value, 0, value.Length, ownedArray.Array, ownedArray.Offset);
                Debug.Assert(nRetrivedBytes == nEncodedBytes);
            }
            writer.Advance(nEncodedBytes);
#endif
            return nTotalBytes;
        }

        private static int WriteUInt32Varint(IBufferWriter<byte> writer, uint value)
        {
            var buffer = writer.GetSpan(5);

            int index = 0;
            do
            {
                buffer[index++] = (byte)((value & 0x7F) | 0x80);
            } while ((value >>= 7) != 0);

            buffer[index - 1] &= 0x7F;

            writer.Advance(index);
            return index;
        }

        internal readonly struct WriteState
        {
            internal readonly int HeaderLength;

            internal readonly Memory<byte> PayloadLengthSpan;

            internal readonly Memory<byte> FrameLengthSpan;

            internal WriteState(int headerLength, in Memory<byte> payloadLengthSpan, in Memory<byte> frameLengthSpan)
            {
                this.HeaderLength = headerLength;
                this.PayloadLengthSpan = payloadLengthSpan;
                this.FrameLengthSpan = frameLengthSpan;
            }
        }
        //public override bool Equals(object obj)
        //{
        //    ThrowNoEquals();
        //    return false;
        //}
        //public override int GetHashCode()
        //{
        //    ThrowNoEquals();
        //    return 0;
        //}
        //public static bool operator ==(LightweightRpcFrame left, LightweightRpcFrame right)
        //{
        //    ThrowNoEquals();
        //    return false;
        //}
        //public static bool operator !=(LightweightRpcFrame left, LightweightRpcFrame right)
        //{
        //    ThrowNoEquals();
        //    return false;
        //}
    }

#pragma warning disable CA1028 // Enum Storage should be Int32
    internal enum RpcFrameType : short
    {
        UnaryRequest,
        UnaryResponse,

        StreamingRequest,
        StreamingResponse,
        StreamingEnd,

        CancelRequest,
        CancelResponse,

        ErrorResponse
    }

    [Flags]
    internal enum RpcOperationFlags
    {
        None = 0,
        CanCancel = 0x0001
    }
#pragma warning restore CA1028 // Enum Storage should be Int32

}
