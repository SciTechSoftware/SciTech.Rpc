﻿#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using Microsoft.Win32.SafeHandles;
using SciTech.Buffers;
using SciTech.Rpc.Client;
using SciTech.Rpc.Serialization;
using SciTech.Threading;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Internal
{
    internal enum RpcFrameState
    {
        /// <summary>
        /// Indicates that no frame information is available.
        /// </summary>
        None,

        /// <summary>
        /// Indicates that only the frame header is available, including FrameType, FrameLength, and MessageNumber
        /// </summary>
        Header,

        /// <summary>
        /// Indicates that full frame information is available.
        /// </summary>
        Full
    }

    /// <summary>
    /// short RPCVersion: Currently always 1
    /// short RpcFrameType: Frame type (from the <see cref="RpcFrameType"/> enum.
    /// int32 FrameLength: Total length of frame, including header
    /// int32 MessageNumber: Message identifier
    /// int32 PayloadLength: Total length of serialized payload
    /// String Operation: Defines the RPC operation associated with the frame. May be empty if not needed.
    /// varint OperationFlags: Operation flags (from the <see cref="RpcOperationFlags"/> enum.
    /// varint Timeout: Operation timeout, in milliseconds (0 means no timeout)
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

        internal LightweightRpcFrame(
            RpcFrameType frameType,
            int frameLength,
            int messageNumber)
        {
            this.FrameType = frameType;
            this.FrameLength = frameLength;
            this.RpcOperation = "";
            this.MessageNumber = messageNumber;
            this.Payload = ReadOnlySequence<byte>.Empty;
            this.Headers = null;

            this.OperationFlags = 0;
            this.Timeout = 0;
        }

        public LightweightRpcFrame(
            RpcFrameType frameType,
            int messageNumber, string rpcOperation,
            IReadOnlyCollection<KeyValuePair<string, ImmutableArray<byte>>>? headers)
        {
            this.FrameType = frameType;
            this.FrameLength = null;
            this.RpcOperation = rpcOperation;
            this.MessageNumber = messageNumber;
            this.Payload = ReadOnlySequence<byte>.Empty;
            this.Headers = headers;

            this.OperationFlags = 0;
            this.Timeout = 0;
        }

        public LightweightRpcFrame(
            RpcFrameType frameType, int messageNumber, string rpcOperation, RpcOperationFlags flags, uint timeout,
            IReadOnlyCollection<KeyValuePair<string, ImmutableArray<byte>>>? headers)
        {
            this.FrameType = frameType;
            this.FrameLength = null;
            this.RpcOperation = rpcOperation;
            this.MessageNumber = messageNumber;
            this.OperationFlags = flags;
            this.Timeout = timeout;
            this.Payload = ReadOnlySequence<byte>.Empty;
            this.Headers = headers;
        }

        public LightweightRpcFrame(
            RpcFrameType frameType,
            int? frameLength,
            int messageNumber, string rpcOperation, RpcOperationFlags flags, uint timeout,
            in ReadOnlySequence<byte> payload, 
            IReadOnlyCollection<KeyValuePair<string, ImmutableArray<byte>>> headers)
        {
            this.FrameType = frameType;
            this.FrameLength = frameLength;
            this.RpcOperation = rpcOperation;
            this.MessageNumber = messageNumber;
            this.Payload = payload;
            this.Headers = headers;

            this.OperationFlags = flags;
            this.Timeout = timeout;
        }

        public int? FrameLength { get; }

        public RpcFrameType FrameType { get; }

        public IReadOnlyCollection<KeyValuePair<string, ImmutableArray<byte>>>? Headers { get; }

        public string? GetHeaderString(string key)
        {
            if (this.Headers != null)
            {
                foreach (var pair in this.Headers)
                {
                    if (pair.Key == key)
                    {
                        return RpcRequestContext.StringFromHeaderBytes(pair.Value);
                    }
                }
            }

            return null;
        }
        
        public ImmutableArray<byte> GetHeaderBytes(string key)
        {
            if (this.Headers != null)
            {
                foreach (var pair in this.Headers)
                {
                    if (pair.Key == key)
                    {
                        return pair.Value;
                    }
                }
            }

            return default;
        }

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
        /// <param name="input">Input sequence containing frame data. Will be updated to the end of the frame
        /// if <see cref="RpcFrameState.Full"/> is returned.</param>
        /// <param name="maxFrameLength"></param>
        /// <param name="message"></param>
        /// <returns>An <see cref="RpcFrameState"/> indicating how much of the frame is available in <paramref name="input"/></returns>
        /// <exception cref="InvalidDataException">Thrown if the frame contains invalid data (not including the payload), or 
        /// if the frame is too big.</exception>
        public static RpcFrameState TryRead(ref ReadOnlySequence<byte> input, int maxFrameLength, out LightweightRpcFrame message)
        {
            if (input.Length < MinimumHeaderLength)
            {
                message = default;
                return RpcFrameState.None;
            }

            var currInput = input;
            short version = ReadInt16LittleEndian(ref currInput);
            if (!IsValidVersion(version))
            {
                throw new InvalidDataException($"Invalid RPC version {version}.");
            }

            var frameType = (RpcFrameType)ReadInt16LittleEndian(ref currInput);
            int frameLength = ReadInt32LittleEndian(ref currInput);
            if (frameLength < 0)
            {
                throw new InvalidDataException($"Invalid RPC frame length: {frameLength}");
            }

            var messageNumber = ReadInt32LittleEndian(ref currInput);
            var payloadLength = ReadInt32LittleEndian(ref currInput);

            // Check the frame length against the available input.
            // Should really be input (and not currInput), since frameLength includes the header and payload.
            if (frameLength > input.Length || frameLength > maxFrameLength)
            {
                message = new LightweightRpcFrame(frameType, frameLength, messageNumber);
                return RpcFrameState.Header;
            }

            var operation = ReadString(ref currInput, frameLength);

            var operationFlags = (RpcOperationFlags)ReadUInt32Varint(ref currInput);
            var timeout = ReadUInt32Varint(ref currInput);

            uint headerPairsCount = ReadUInt32Varint(ref currInput);
            if (headerPairsCount * 2 > frameLength)
            {
                // Clearly not correct. An empty string is one byte, so we have more pairs than 
                // could possibly fit in the frame.
                throw new InvalidDataException("Too many header pairs.");
            }

            ImmutableArray<KeyValuePair<string, ImmutableArray<byte>>> headers;

            if (headerPairsCount > 0)
            {
                var pairsBuilder = ImmutableArray.CreateBuilder<KeyValuePair<string, ImmutableArray<byte>>>((int)headerPairsCount);
                for (int pairIndex = 0; pairIndex < headerPairsCount; pairIndex++)
                {
                    string key = ReadString(ref currInput, frameLength);
                    var value = ReadBytes(ref currInput, frameLength);
                    pairsBuilder.Add(new KeyValuePair<string, ImmutableArray<byte>>(key, value));
                }

                headers = pairsBuilder.MoveToImmutable();
            }
            else
            {
                headers = ImmutableArray<KeyValuePair<string, ImmutableArray<byte>>>.Empty;
            }


            var payload = currInput.Slice(0, payloadLength);
            input = currInput.Slice(payloadLength);
            message = new LightweightRpcFrame(frameType, frameLength, messageNumber, operation, operationFlags, timeout, payload, headers);
            return RpcFrameState.Full;
        }

        internal static async Task<LightweightRpcFrame> ReadFrameAsync(Stream stream, int maxFrameSize, CancellationToken cancellationToken)
        {
            var reader = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
            try
            {
                return await ReadFrameAsync(reader, maxFrameSize, cancellationToken).ContextFree();
            }
            finally
            {
                await reader.CompleteAsync().ContextFree();
            }
        }


        internal static async Task<LightweightRpcFrame> ReadFrameAsync(PipeReader reader, int maxFrameSize, CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var readResult = await reader.ReadAsync(cancellationToken).ContextFree();

                var buffer = readResult.Buffer;
                var state = LightweightRpcFrame.TryRead(ref buffer, maxFrameSize, out var frame);
                switch (state)
                {
                    case RpcFrameState.Full:
                        reader.AdvanceTo(buffer.Start);
                        return frame;
                    default:
                        reader.AdvanceTo(buffer.Start, buffer.End);
                        break;
                }
            }
        }
        public static RpcFrameState TryRead(ReadOnlyMemory<byte> input, int maxFrameLength, out LightweightRpcFrame message)
        {
            var sequence = new ReadOnlySequence<byte>(input);
            return TryRead( ref sequence, maxFrameLength, out message);
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
                    headerLength += WriteBytes(writer, pair.Value);
                }
            }

            return new WriteState(headerLength, payloadLengthSpan, frameLengthSpan, writer);
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
                Span<byte> local = stackalloc byte[4];

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

        private static ImmutableArray<byte> ReadBytes(ref ReadOnlySequence<byte> input, int maxLength)
        {
            uint length = ReadUInt32Varint(ref input);
            if (length > maxLength)
            {
                throw new InvalidDataException();
            }

            ImmutableArray<byte> value;
            byte[] buffer = new byte[length];
            input.Slice(0, length).CopyTo(buffer);
            value = buffer.ToImmutableArray();

            input = input.Slice(length);
            return value;
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

        private static int WriteBytes(IBufferWriter<byte> writer, ImmutableArray<byte> bytes)
        {
            int nBytes = bytes.Length;
            int nTotalBytes = WriteUInt32Varint(writer, (uint)nBytes);
            nTotalBytes += nBytes;

            var destSpan = writer.GetSpan(nBytes);
            bytes.AsSpan().CopyTo(destSpan);
            writer.Advance(nBytes);

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

            internal readonly BufferWriterStream Writer;

            internal WriteState(int headerLength, in Memory<byte> payloadLengthSpan, in Memory<byte> frameLengthSpan, BufferWriterStream writer)
            {
                this.HeaderLength = headerLength;
                this.PayloadLengthSpan = payloadLengthSpan;
                this.FrameLengthSpan = frameLengthSpan;
                this.Writer = writer;
            }

            internal bool IsEmpty => this.Writer != null;
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

        TimeoutResponse,

        ErrorResponse,

        ServiceDiscoveryRequest,

        ServiceDiscoveryResponse,

        ConnectionRequest,
        ConnectionResponse,
    }

    [Flags]
    internal enum RpcOperationFlags
    {
        None = 0,
        CanCancel = 0x0001
    }
#pragma warning restore CA1028 // Enum Storage should be Int32

}
