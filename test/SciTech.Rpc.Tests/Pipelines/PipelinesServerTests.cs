using Moq;
using NUnit.Framework;
using SciTech.IO;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Pipelines.Server;
using SciTech.Rpc.Pipelines.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Pipelines
{


    [TestFixture]
    public class PipelinesServerTests
    {
        /// <summary>
        /// Makes a simple RPC call to a pipelines server, by explicitly building the request frame and parsing the response frame.
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task SimpleServiceServerTest()
        {
            Pipe requestPipe = new Pipe();
            Pipe responsePipe = new Pipe();

            var serializer = new ProtobufSerializer();
            var serviceImpl = new TestBlockingSimpleServiceImpl();
            var hostMock = new Mock<IRpcServerImpl>();
            var serviceImplProviderMock = new Mock<IRpcServiceActivator>();
            serviceImplProviderMock.Setup(p => p.GetServiceImpl<ISimpleService>(It.IsAny<IServiceProvider>(), It.IsAny<RpcObjectId>())).Returns(serviceImpl);

            hostMock.Setup(p => p.ServiceImplProvider).Returns(serviceImplProviderMock.Object);
            hostMock.Setup(p => p.CallInterceptors).Returns(ImmutableArray<RpcServerCallInterceptor>.Empty);

            var serviceRegistrator = new RpcServiceDefinitionBuilder();
            serviceRegistrator.RegisterService<ISimpleService>();

            var serverId = RpcServerId.NewId();
            using (var host = new RpcPipelinesServer(Mock.Of<IRpcServicePublisher>(), serviceImplProviderMock.Object, serviceRegistrator, null, new RpcServiceOptions { Serializer = serializer } ))
            {
                host.AddEndPoint(new DirectPipelinesEndPoint(new DirectDuplexPipe(requestPipe.Reader, responsePipe.Writer)));

                host.Start();

                var objectId = RpcObjectId.NewId();

                var requestFrame = new RpcPipelinesFrame(RpcFrameType.UnaryRequest, 1, "SciTech.Rpc.Tests.SimpleService.Add", ImmutableArray<KeyValuePair<string, string>>.Empty);

                var writer = requestPipe.Writer;
                var writeState = requestFrame.BeginWrite(writer);

                var request = new RpcObjectRequest<int, int>(objectId, 5, 6);
                int payloadLength;
                using (var payloadStream = new CountedBufferWriterStream(writer))
                {
                    serializer.ToStream(payloadStream, request);
                    payloadLength = checked((int)payloadStream.Length);
                }

                RpcPipelinesFrame.EndWrite(payloadLength, writeState);

                await writer.FlushAsync();

                RpcResponse<int> response = null;
                while (response == null)
                {
                    var readResult = await responsePipe.Reader.ReadAsync();

                    if (!readResult.IsCanceled)
                    {
                        var buffer = readResult.Buffer;
                        if (RpcPipelinesFrame.TryRead(ref buffer, RpcPipelinesFrame.DefaultMaxFrameLength, out var responseFrame))
                        {
                            Assert.AreEqual(requestFrame.RpcOperation, responseFrame.RpcOperation);
                            Assert.AreEqual(requestFrame.MessageNumber, responseFrame.MessageNumber);

                            using (var responseStream = responseFrame.Payload.AsStream())
                            {
                                response = (RpcResponse<int>)serializer.FromStream(typeof(RpcResponse<int>), responseStream);
                            }

                            responsePipe.Reader.AdvanceTo(buffer.Start);
                        }
                        else
                        {
                            if( readResult.IsCompleted)
                            {
                                break;
                            } 

                            responsePipe.Reader.AdvanceTo(buffer.Start, buffer.End);
                        }
                    }
                    else
                    {
                        // Not expected
                        break;
                    }
                }

                Assert.NotNull(response);
                Assert.AreEqual(11, response.Result);
            }
        }
    }
}
