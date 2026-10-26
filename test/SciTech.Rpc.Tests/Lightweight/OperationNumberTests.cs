using Moq;
using NUnit.Framework;
using SciTech.Collections.Immutable;
using SciTech.IO;
using SciTech.Rpc.Client;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Lightweight;
using SciTech.Rpc.Lightweight.Client.Internal;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Lightweight.Server;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Serialization.Internal;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Lightweight
{
    /// <summary>
    /// Tests the operation number extension of the Lightweight protocol (section 6 of docs/lightweight-protocol.md).
    /// </summary>
    [TestFixture]
    public class OperationNumberTests
    {
        private const string AddOperation = "SciTech.Rpc.Tests.SimpleService.Add";

        private static readonly IReadOnlyCollection<KeyValuePair<string, ImmutableArray<byte>>> NoHeaders
            = ImmutableArray<KeyValuePair<string, ImmutableArray<byte>>>.Empty;

        private readonly IRpcSerializer serializer = new ProtobufRpcSerializer();

        [Test]
        public void Frame_WithOperationNumber_RoundTrips()
        {
            // 300 is encoded as a two-byte varint.
            var frame = new LightweightRpcFrame(RpcFrameType.UnaryRequest, 12, "", 300, RpcOperationFlags.CanCancel, 5000, NoHeaders);

            var data = WriteFrame(frame);

            Assert.AreEqual(RpcFrameState.Full, LightweightRpcFrame.TryRead(data, LightweightRpcFrame.DefaultMaxFrameLength, out var readFrame));
            Assert.AreEqual(RpcFrameType.UnaryRequest, readFrame.FrameType);
            Assert.AreEqual(12, readFrame.MessageNumber);
            Assert.AreEqual("", readFrame.RpcOperation);
            Assert.AreEqual(300u, readFrame.OperationNumber);
            Assert.AreEqual(RpcOperationFlags.CanCancel | RpcOperationFlags.HasOperationNumber, readFrame.OperationFlags);
            Assert.AreEqual(5000u, readFrame.Timeout);
            Assert.AreEqual(data.Length, readFrame.FrameLength);
        }

        [Test]
        public void Frame_WithoutOperationNumber_HasNoNumberField()
        {
            var frame = new LightweightRpcFrame(RpcFrameType.UnaryRequest, 1, "", RpcOperationFlags.AcceptsOperationNumber, 0, NoHeaders);

            var data = WriteFrame(frame);

            // Fixed header, empty operation, flags, timeout and header count. No operation number.
            Assert.AreEqual(LightweightRpcFrame.MinimumHeaderLength + 4, data.Length);
            Assert.AreEqual(RpcFrameState.Full, LightweightRpcFrame.TryRead(data, LightweightRpcFrame.DefaultMaxFrameLength, out var readFrame));
            Assert.AreEqual(0u, readFrame.OperationNumber);
            Assert.AreEqual(RpcOperationFlags.AcceptsOperationNumber, readFrame.OperationFlags);
        }

        [Test]
        public void Frame_WithZeroOperationNumber_IsInvalid()
        {
            var data = WriteFrame(new LightweightRpcFrame(RpcFrameType.UnaryRequest, 1, "", 1, RpcOperationFlags.None, 0, NoHeaders));

            // The operation number follows the fixed header, the empty operation name and the flags.
            int numberOffset = LightweightRpcFrame.MinimumHeaderLength + 2;
            Assert.AreEqual(1, data[numberOffset]);
            data[numberOffset] = 0;

            Assert.Throws<InvalidDataException>(() => LightweightRpcFrame.TryRead(data, LightweightRpcFrame.DefaultMaxFrameLength, out _));
        }

        /// <summary>
        /// A request from a client without the extension must be answered as before: the name is echoed and no number is added.
        /// </summary>
        [Test]
        public async Task Server_RequestWithoutAcceptsFlag_EchoesNameWithoutNumber()
        {
            await using var server = await TestServer.StartAsync(this.serializer);

            var response = await server.CallAddAsync(new LightweightRpcFrame(RpcFrameType.UnaryRequest, 1, AddOperation, RpcOperationFlags.None, 0, NoHeaders));

            Assert.AreEqual(RpcFrameType.UnaryResponse, response.FrameType);
            Assert.AreEqual(AddOperation, response.Operation);
            Assert.AreEqual(0u, response.OperationNumber);
            Assert.AreEqual(RpcOperationFlags.None, response.Flags & RpcOperationFlags.HasOperationNumber);
            Assert.AreEqual(11, response.Result);
        }

        [Test]
        public async Task Server_RequestByNameWithAcceptsFlag_ReturnsNumber_AndAcceptsRequestByNumber()
        {
            await using var server = await TestServer.StartAsync(this.serializer);

            var firstResponse = await server.CallAddAsync(new LightweightRpcFrame(RpcFrameType.UnaryRequest, 1, AddOperation, RpcOperationFlags.AcceptsOperationNumber, 0, NoHeaders));

            Assert.AreEqual(RpcFrameType.UnaryResponse, firstResponse.FrameType);
            Assert.AreEqual("", firstResponse.Operation);
            Assert.AreNotEqual(0u, firstResponse.OperationNumber);
            Assert.AreEqual(11, firstResponse.Result);

            var secondResponse = await server.CallAddAsync(new LightweightRpcFrame(RpcFrameType.UnaryRequest, 2, "", firstResponse.OperationNumber, RpcOperationFlags.None, 0, NoHeaders));

            Assert.AreEqual(RpcFrameType.UnaryResponse, secondResponse.FrameType);
            Assert.AreEqual(2, secondResponse.MessageNumber);
            Assert.AreEqual("", secondResponse.Operation);
            Assert.AreEqual(0u, secondResponse.OperationNumber, "A request by number should not get the number back.");
            Assert.AreEqual(11, secondResponse.Result);
        }

        /// <summary>
        /// A server with operation numbers disabled must behave as a server without the extension.
        /// </summary>
        [Test]
        public async Task Server_OperationNumbersDisabled_EchoesNameWithoutNumber()
        {
            await using var server = await TestServer.StartAsync(this.serializer, new LightweightOptions { UseOperationNumbers = false });

            var response = await server.CallAddAsync(new LightweightRpcFrame(RpcFrameType.UnaryRequest, 1, AddOperation, RpcOperationFlags.AcceptsOperationNumber, 0, NoHeaders));

            Assert.AreEqual(RpcFrameType.UnaryResponse, response.FrameType);
            Assert.AreEqual(AddOperation, response.Operation);
            Assert.AreEqual(0u, response.OperationNumber);
            Assert.AreEqual(11, response.Result);
        }

        [Test]
        public async Task Server_UnknownOperationNumber_ReturnsRemoteDefinitionError()
        {
            await using var server = await TestServer.StartAsync(this.serializer);

            var response = await server.CallAddAsync(new LightweightRpcFrame(RpcFrameType.UnaryRequest, 1, "", 10000, RpcOperationFlags.None, 0, NoHeaders));

            Assert.AreEqual(RpcFrameType.ErrorResponse, response.FrameType);
            Assert.AreEqual(nameof(RpcFailure.RemoteDefinitionError), response.ErrorCode);
        }

        [Test]
        public async Task Server_OperationNumberAndName_ReturnsInvalidData()
        {
            await using var server = await TestServer.StartAsync(this.serializer);

            var response = await server.CallAddAsync(new LightweightRpcFrame(RpcFrameType.UnaryRequest, 1, AddOperation, 1, RpcOperationFlags.None, 0, NoHeaders));

            Assert.AreEqual(RpcFrameType.ErrorResponse, response.FrameType);
            Assert.AreEqual(nameof(RpcFailure.InvalidData), response.ErrorCode);
        }

        [Test]
        public async Task Client_FirstRequestByName_ThenByNumber()
        {
            await using var connection = TestClientConnection.Start(this.serializer);

            var firstCall = connection.CallAddAsync();
            var firstRequest = await connection.ReadRequestAsync();
            Assert.AreEqual(AddOperation, firstRequest.Operation);
            Assert.AreEqual(RpcOperationFlags.AcceptsOperationNumber, firstRequest.Flags & (RpcOperationFlags.AcceptsOperationNumber | RpcOperationFlags.HasOperationNumber));
            await connection.WriteResponseAsync(firstRequest.MessageNumber, 7);
            Assert.AreEqual(11, (await firstCall.DefaultTimeout()).Result);

            var secondCall = connection.CallAddAsync();
            var secondRequest = await connection.ReadRequestAsync();
            Assert.AreEqual("", secondRequest.Operation);
            Assert.AreEqual(7u, secondRequest.OperationNumber);
            Assert.AreEqual(RpcOperationFlags.None, secondRequest.Flags & RpcOperationFlags.AcceptsOperationNumber);
            await connection.WriteResponseAsync(secondRequest.MessageNumber, 0);
            Assert.AreEqual(11, (await secondCall.DefaultTimeout()).Result);
        }

        /// <summary>
        /// A server without the extension never returns a number, so the client must keep sending the name.
        /// </summary>
        [Test]
        public async Task Client_ServerWithoutOperationNumbers_KeepsSendingName()
        {
            await using var connection = TestClientConnection.Start(this.serializer);

            for (int i = 0; i < 2; i++)
            {
                var call = connection.CallAddAsync();
                var request = await connection.ReadRequestAsync();
                Assert.AreEqual(AddOperation, request.Operation);
                Assert.AreEqual(0u, request.OperationNumber);
                Assert.AreNotEqual(RpcOperationFlags.None, request.Flags & RpcOperationFlags.AcceptsOperationNumber);

                // Respond like an older server: echo the name and don't add a number.
                await connection.WriteResponseAsync(request.MessageNumber, 0, AddOperation);
                Assert.AreEqual(11, (await call.DefaultTimeout()).Result);
            }
        }

        /// <summary>
        /// A client with operation numbers disabled must behave as a client without the extension, even if the server
        /// (incorrectly) returns a number.
        /// </summary>
        [Test]
        public async Task Client_OperationNumbersDisabled_SendsNameWithoutAcceptsFlag()
        {
            await using var connection = TestClientConnection.Start(this.serializer, useOperationNumbers: false);

            for (int i = 0; i < 2; i++)
            {
                var call = connection.CallAddAsync();
                var request = await connection.ReadRequestAsync();
                Assert.AreEqual(AddOperation, request.Operation);
                Assert.AreEqual(RpcOperationFlags.None, request.Flags & (RpcOperationFlags.AcceptsOperationNumber | RpcOperationFlags.HasOperationNumber));

                await connection.WriteResponseAsync(request.MessageNumber, 7, AddOperation);
                Assert.AreEqual(11, (await call.DefaultTimeout()).Result);
            }
        }

        [Test]
        public async Task Client_UnknownOperationNumberError_SendsNameAgain()
        {
            await using var connection = TestClientConnection.Start(this.serializer);

            var firstCall = connection.CallAddAsync();
            var firstRequest = await connection.ReadRequestAsync();
            await connection.WriteResponseAsync(firstRequest.MessageNumber, 7);
            await firstCall.DefaultTimeout();

            var failingCall = connection.CallAddAsync();
            var failingRequest = await connection.ReadRequestAsync();
            Assert.AreEqual(7u, failingRequest.OperationNumber);
            await connection.WriteUnknownOperationErrorAsync(failingRequest.MessageNumber);
            Assert.ThrowsAsync<RpcErrorException>(() => failingCall.DefaultTimeout());

            var thirdCall = connection.CallAddAsync();
            var thirdRequest = await connection.ReadRequestAsync();
            Assert.AreEqual(AddOperation, thirdRequest.Operation);
            Assert.AreEqual(0u, thirdRequest.OperationNumber);
            await connection.WriteResponseAsync(thirdRequest.MessageNumber, 0);
            await thirdCall.DefaultTimeout();
        }

        /// <summary>
        /// Streaming responses only need to return the number in the first frame.
        /// </summary>
        [Test]
        public async Task Client_StreamingCall_LearnsNumberFromFirstResponse()
        {
            await using var connection = TestClientConnection.Start(this.serializer);

            var serializers = new LightweightSerializers<RpcObjectRequest<int, int>, RpcResponse<int>>(this.serializer);
            await using (var streamingCall = connection.Client.BeginStreamingServerCall(
                RpcFrameType.ServerStreamingRequest, AddOperation, null, new RpcObjectRequest<int, int>(RpcObjectId.NewId(), 5, 6), serializers, 0, false))
            {
                var request = await connection.ReadRequestAsync();
                Assert.AreEqual(AddOperation, request.Operation);

                await connection.WriteFrameAsync(new LightweightRpcFrame(RpcFrameType.StreamingResponse, request.MessageNumber, "", 9, RpcOperationFlags.None, 0, NoHeaders), new RpcResponse<int>(1));
                await connection.WriteFrameAsync(new LightweightRpcFrame(RpcFrameType.StreamingResponse, request.MessageNumber, "", RpcOperationFlags.None, 0, NoHeaders), new RpcResponse<int>(2));
                await connection.WriteFrameAsync(new LightweightRpcFrame(RpcFrameType.StreamingEnd, request.MessageNumber, "", RpcOperationFlags.None, 0, NoHeaders), null);

                var results = new List<int>();
                while (await streamingCall.ResponseStream.MoveNextAsync().AsTask().DefaultTimeout())
                {
                    results.Add(streamingCall.ResponseStream.Current.Result);
                }

                CollectionAssert.AreEqual(new[] { 1, 2 }, results);
            }

            var call = connection.CallAddAsync();
            var secondRequest = await connection.ReadRequestAsync();
            Assert.AreEqual(9u, secondRequest.OperationNumber);
            await connection.WriteResponseAsync(secondRequest.MessageNumber, 0);
            await call.DefaultTimeout();
        }

        private static byte[] WriteFrame(in LightweightRpcFrame frame)
        {
            using var frameWriter = new LightweightRpcFrameWriter(LightweightRpcFrame.DefaultMaxFrameLength);
            return frameWriter.WriteFrame(frame);
        }

        /// <summary>
        /// Reads a frame from <paramref name="reader"/>, and deserializes its payload before the frame data is released.
        /// </summary>
        private static async Task<ReceivedFrame> ReadFrameAsync(PipeReader reader, IRpcSerializer serializer, Type? payloadType)
        {
            while (true)
            {
                var readResult = await reader.ReadAsync().AsTask().DefaultTimeout();
                var buffer = readResult.Buffer;
                if (LightweightRpcFrame.TryRead(ref buffer, LightweightRpcFrame.DefaultMaxFrameLength, out var frame) == RpcFrameState.Full)
                {
                    var received = new ReceivedFrame(frame,
                        payloadType != null && frame.Payload.Length > 0 ? serializer.Deserialize(frame.Payload, payloadType) : null);
                    reader.AdvanceTo(buffer.Start);
                    return received;
                }

                if (readResult.IsCompleted)
                {
                    throw new EndOfStreamException();
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
            }
        }

        private sealed class ReceivedFrame
        {
            internal ReceivedFrame(in LightweightRpcFrame frame, object? payload)
            {
                this.FrameType = frame.FrameType;
                this.MessageNumber = frame.MessageNumber;
                this.Operation = frame.RpcOperation;
                this.OperationNumber = frame.OperationNumber;
                this.Flags = frame.OperationFlags;
                this.ErrorCode = frame.GetHeaderString(WellKnownHeaderKeys.ErrorCode);
                this.Payload = payload;
            }

            internal string? ErrorCode { get; }

            internal RpcOperationFlags Flags { get; }

            internal RpcFrameType FrameType { get; }

            internal int MessageNumber { get; }

            internal string Operation { get; }

            internal uint OperationNumber { get; }

            internal object? Payload { get; }

            internal int Result => ((RpcResponse<int>)this.Payload!).Result;
        }

        /// <summary>
        /// A <see cref="LightweightRpcServer"/> publishing <see cref="ISimpleService"/>, called by writing request frames
        /// directly to its pipe.
        /// </summary>
        private sealed class TestServer : IAsyncDisposable
        {
            private readonly LightweightRpcServer host;

            private readonly Pipe requestPipe = new Pipe();

            private readonly Pipe responsePipe = new Pipe();

            private readonly IRpcSerializer serializer;

            private TestServer(IRpcSerializer serializer, LightweightOptions? lightweightOptions)
            {
                this.serializer = serializer;

                var serviceImpl = new TestBlockingSimpleServiceImpl();
                var serviceImplProviderMock = new Mock<IRpcServiceActivator>();
                serviceImplProviderMock.Setup(p => p.GetActivatedService<ISimpleService>(It.IsAny<IServiceProvider>(), It.IsAny<RpcObjectId>()))
                    .Returns(new ActivatedService<ISimpleService>(serviceImpl, null));

                var serviceRegistrator = new RpcServiceDefinitionsBuilder();
                serviceRegistrator.RegisterService<ISimpleService>();

                this.host = new LightweightRpcServer(Mock.Of<IRpcServicePublisher>(), serviceImplProviderMock.Object, serviceRegistrator, null,
                    new RpcServerOptions { Serializer = serializer }, lightweightOptions);
                this.host.AddEndPoint(new InprocRpcEndPoint(new DirectDuplexPipe(this.requestPipe.Reader, this.responsePipe.Writer), false));
            }

            internal static Task<TestServer> StartAsync(IRpcSerializer serializer, LightweightOptions? lightweightOptions = null)
            {
                var server = new TestServer(serializer, lightweightOptions);
                server.host.Start();
                return Task.FromResult(server);
            }

            /// <summary>
            /// Sends a request for Add(5, 6) using the supplied request frame header, and returns the response frame.
            /// </summary>
            internal async Task<ReceivedFrame> CallAddAsync(LightweightRpcFrame requestFrame)
            {
                using (var frameWriter = new BufferWriterStreamImpl())
                {
                    var writeState = requestFrame.BeginWrite(frameWriter);
                    var request = new RpcObjectRequest<int, int>(RpcObjectId.NewId(), 5, 6);
                    this.serializer.Serialize(frameWriter, request, request.GetType());
                    LightweightRpcFrame.EndWrite(checked((int)frameWriter.Length), writeState);
                    frameWriter.CopyTo(this.requestPipe.Writer);
                }

                await this.requestPipe.Writer.FlushAsync();

                var response = await ReadFrameAsync(this.responsePipe.Reader, this.serializer, typeof(RpcResponse<int>));
                Assert.AreEqual(requestFrame.MessageNumber, response.MessageNumber);
                return response;
            }

            public async ValueTask DisposeAsync()
            {
                await this.requestPipe.Writer.CompleteAsync();
                await this.host.ShutdownAsync();
                this.host.Dispose();
            }
        }

        /// <summary>
        /// An <see cref="RpcPipelineClient"/> connected to a scripted server: the test reads the client's request frames
        /// and writes the response frames.
        /// </summary>
        private sealed class TestClientConnection : IAsyncDisposable
        {
            private readonly Pipe requestPipe = new Pipe();

            private readonly Pipe responsePipe = new Pipe();

            private readonly IRpcSerializer serializer;

            private readonly LightweightSerializers<RpcObjectRequest<int, int>, RpcResponse<int>> serializers;

            private Task? runTask;

            private TestClientConnection(IRpcSerializer serializer, bool useOperationNumbers)
            {
                this.serializer = serializer;
                this.serializers = new LightweightSerializers<RpcObjectRequest<int, int>, RpcResponse<int>>(serializer);
                this.Client = new RpcPipelineClient(new DirectDuplexPipe(this.responsePipe.Reader, this.requestPipe.Writer), null, null, false, useOperationNumbers);
            }

            internal RpcPipelineClient Client { get; }

            internal static TestClientConnection Start(IRpcSerializer serializer, bool useOperationNumbers = true)
            {
                var connection = new TestClientConnection(serializer, useOperationNumbers);
                connection.runTask = connection.Client.RunAsyncCore();
                return connection;
            }

            internal Task<RpcResponse<int>> CallAddAsync()
                => this.Client.SendReceiveFrameAsync(
                    RpcFrameType.UnaryRequest, AddOperation, null, new RpcObjectRequest<int, int>(RpcObjectId.NewId(), 5, 6), this.serializers, 0);

            internal Task<ReceivedFrame> ReadRequestAsync()
                => ReadFrameAsync(this.requestPipe.Reader, this.serializer, null);

            /// <summary>
            /// Writes a successful response to Add(5, 6).
            /// </summary>
            internal Task WriteResponseAsync(int messageNumber, uint operationNumber, string operation = "")
                => this.WriteFrameAsync(
                    new LightweightRpcFrame(RpcFrameType.UnaryResponse, messageNumber, operation, operationNumber, RpcOperationFlags.None, 0, NoHeaders),
                    new RpcResponse<int>(11));

            /// <summary>
            /// Writes the error response of a server that doesn't recognize the requested operation.
            /// </summary>
            internal Task WriteUnknownOperationErrorAsync(int messageNumber)
            {
                var headers = new List<KeyValuePair<string, ImmutableArray<byte>>>
                {
                    new KeyValuePair<string, ImmutableArray<byte>>(WellKnownHeaderKeys.ErrorType, RpcRequestContext.ToHeaderBytes(WellKnownRpcErrors.Failure)),
                    new KeyValuePair<string, ImmutableArray<byte>>(WellKnownHeaderKeys.ErrorCode, RpcRequestContext.ToHeaderBytes(nameof(RpcFailure.RemoteDefinitionError))),
                    new KeyValuePair<string, ImmutableArray<byte>>(WellKnownHeaderKeys.ErrorMessage, RpcRequestContext.ToHeaderBytes("Unknown RPC operation number.")),
                };

                return this.WriteFrameAsync(new LightweightRpcFrame(RpcFrameType.ErrorResponse, messageNumber, "", headers), null);
            }

            internal async Task WriteFrameAsync(LightweightRpcFrame frame, RpcResponse<int>? payload)
            {
                using var frameWriter = new LightweightRpcFrameWriter(LightweightRpcFrame.DefaultMaxFrameLength);
                var data = payload != null ? frameWriter.WriteFrame(frame, payload, this.serializer) : frameWriter.WriteFrame(frame);
                await this.responsePipe.Writer.WriteAsync(data);
            }

            public async ValueTask DisposeAsync()
            {
                await this.responsePipe.Writer.CompleteAsync();
                if (this.runTask != null)
                {
                    await Task.WhenAny(this.runTask, Task.Delay(TimeSpan.FromSeconds(5)));
                }
            }
        }
    }
}
