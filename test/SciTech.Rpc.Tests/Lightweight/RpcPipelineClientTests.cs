using NUnit.Framework;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Lightweight.Client.Internal;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Serialization;
using System;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Lightweight
{
    [TestFixture]
    public class RpcPipelineClientTests
    {
        private static readonly TimeSpan FinishTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// When the remote side closes the connection, the receive loop ends by itself and closes the pipeline from its
        /// own finally block. That close must not wait for the receive loop, or the loop waits for itself and every later
        /// wait for the pipeline, such as the one in <c>LightweightRpcConnection.ShutdownAsync</c>, hangs forever. The
        /// close must also end pending calls; before the fix it never got that far, so open streams neither received data
        /// nor failed.
        /// </summary>
        [Test]
        public async Task ReceiveLoop_Should_Finish_WhenRemoteClosesConnection()
        {
            var requestPipe = new Pipe();
            var responsePipe = new Pipe();
            var client = new RpcPipelineClient(new DirectDuplexPipe(responsePipe.Reader, requestPipe.Writer), null, null, false);
            var runTask = client.RunAsyncCore();

            var serializers = new LightweightSerializers<RpcObjectRequest<int, int>, RpcResponse<int>>(new ProtobufRpcSerializer());
            await using var streamingCall = client.BeginStreamingServerCall(
                RpcFrameType.ServerStreamingRequest, "SciTech.Rpc.Tests.StreamingService.GetNumbers", null,
                new RpcObjectRequest<int, int>(RpcObjectId.NewId(), 1, 2), serializers, 0, false);
            var moveNextTask = streamingCall.ResponseStream.MoveNextAsync().AsTask();

            // The remote side closes the connection.
            await responsePipe.Writer.CompleteAsync();

            var finishedTask = client.WaitFinishedAsync();
            Assert.That(
                await Task.WhenAny(finishedTask, Task.Delay(FinishTimeout)), Is.SameAs(finishedTask),
                "The receive loop did not finish after the remote side closed the connection.");
            Assert.That(client.IsClosed, Is.True);

            Assert.That(
                await Task.WhenAny(runTask, Task.Delay(FinishTimeout)), Is.SameAs(runTask),
                "RunAsyncCore did not finish after the remote side closed the connection.");

            Assert.That(
                await Task.WhenAny(moveNextTask, Task.Delay(FinishTimeout)), Is.SameAs(moveNextTask),
                "The pending streaming call did not end after the remote side closed the connection.");
            Assert.That(moveNextTask.IsFaulted || moveNextTask.IsCanceled, Is.True, "The pending streaming call ended without an error or cancellation.");
        }
    }
}
