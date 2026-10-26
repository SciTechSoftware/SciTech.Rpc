using NUnit.Framework;
using SciTech.Rpc.Client;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests
{
    public enum StreamingFaultKind
    {
        /// <summary>
        /// An <see cref="RpcFaultException{TFault}"/> of a declared fault.
        /// </summary>
        Declared,

        /// <summary>
        /// A <see cref="DeclaredFaultException"/>, which is converted by <see cref="DeclaredFaultExceptionConverter"/>.
        /// </summary>
        Converted,

        /// <summary>
        /// An exception that is not declared or converted.
        /// </summary>
        Undeclared
    }

    [RpcService]
    public interface IStreamingFaultService
    {
        /// <summary>
        /// Returns <paramref name="count"/> items, and then fails with an exception of <paramref name="faultKind"/>.
        /// </summary>
        [RpcFault(typeof(DeclaredFault))]
        IAsyncEnumerable<SequenceData> GetSequenceWithFault(int count, StreamingFaultKind faultKind);

        /// <summary>
        /// Reads <paramref name="count"/> items, and then fails with an exception of <paramref name="faultKind"/>.
        /// </summary>
        [RpcFault(typeof(DeclaredFault))]
        Task<int> UploadWithFault(int count, StreamingFaultKind faultKind, IAsyncEnumerable<SequenceData> values);
    }

    public class StreamingFaultServiceImpl : IStreamingFaultService
    {
        public async IAsyncEnumerable<SequenceData> GetSequenceWithFault(int count, StreamingFaultKind faultKind)
        {
            for (int i = 0; i < count; i++)
            {
                await Task.Yield();
                yield return new SequenceData(i + 1);
            }

            throw CreateException(faultKind);
        }

        public async Task<int> UploadWithFault(int count, StreamingFaultKind faultKind, IAsyncEnumerable<SequenceData> values)
        {
            if (count > 0)
            {
                int received = 0;
                await foreach (var value in values)
                {
                    if (++received >= count)
                    {
                        break;
                    }
                }
            }

            throw CreateException(faultKind);
        }

        private static Exception CreateException(StreamingFaultKind faultKind) => faultKind switch
        {
            StreamingFaultKind.Declared => new RpcFaultException<DeclaredFault>("Declared fault.", new DeclaredFault("Declared details")),
            StreamingFaultKind.Converted => new DeclaredFaultException("Converted fault.", "Converted details"),
            _ => new InvalidOperationException("Undeclared exception.")
        };
    }

    /// <summary>
    /// Tests that exceptions thrown by the implementation of a streaming operation are handled by the fault handler, as for
    /// unary operations: declared faults and converted exceptions reach the client, and other exceptions become
    /// <see cref="RpcFailureException"/>s.
    /// </summary>
    [TestFixture(RpcConnectionType.LightweightTcp)]
    [TestFixture(RpcConnectionType.LightweightInproc)]
#if NET
    [TestFixture(RpcConnectionType.NetGrpc)]
#endif
    public class StreamingFaultTests : ClientServerTestsBase
    {
        public StreamingFaultTests(RpcConnectionType connectionType)
            : base(new ProtobufRpcSerializer(), connectionType)
        {
        }

        [TestCase(StreamingFaultKind.Declared, 0)]
        [TestCase(StreamingFaultKind.Declared, 3)]
        [TestCase(StreamingFaultKind.Converted, 0)]
        [TestCase(StreamingFaultKind.Converted, 3)]
        [TestCase(StreamingFaultKind.Undeclared, 0)]
        [TestCase(StreamingFaultKind.Undeclared, 3)]
        public async Task ServerStreaming_ImplementationFault_ShouldBeHandled(StreamingFaultKind faultKind, int count)
        {
            await this.RunWithServiceAsync(faultKind, async faultService =>
            {
                var received = new List<int>();
                async Task EnumerateAsync()
                {
                    await foreach (var item in faultService.GetSequenceWithFault(count, faultKind))
                    {
                        received.Add(item.SequenceNo);
                    }
                }

                AssertFault(faultKind, () => EnumerateAsync().DefaultTimeout());

                // The items before the fault are received.
                Assert.AreEqual(count, received.Count);
            });
        }

        [TestCase(StreamingFaultKind.Declared, 0)]
        [TestCase(StreamingFaultKind.Declared, 3)]
        [TestCase(StreamingFaultKind.Converted, 0)]
        [TestCase(StreamingFaultKind.Converted, 3)]
        [TestCase(StreamingFaultKind.Undeclared, 0)]
        [TestCase(StreamingFaultKind.Undeclared, 3)]
        public async Task ClientStreaming_ImplementationFault_ShouldBeHandled(StreamingFaultKind faultKind, int count)
        {
            await this.RunWithServiceAsync(faultKind, faultService =>
            {
                // The implementation fails before the upload is complete.
                AssertFault(faultKind, () => faultService.UploadWithFault(count, faultKind, GenerateSequence(100)).DefaultTimeout());
                return Task.CompletedTask;
            });
        }

        private static void AssertFault(StreamingFaultKind faultKind, AsyncTestDelegate call)
        {
            switch (faultKind)
            {
                case StreamingFaultKind.Declared:
                    var faultException = Assert.ThrowsAsync<RpcFaultException<DeclaredFault>>(call);
                    Assert.AreEqual("Declared fault.", faultException.Message);
                    Assert.AreEqual("Declared details", faultException.Fault.Message);
                    break;
                case StreamingFaultKind.Converted:
                    var convertedException = Assert.ThrowsAsync<DeclaredFaultException>(call);
                    Assert.AreEqual("Converted fault.", convertedException.Message);
                    Assert.AreEqual("Converted details", convertedException.DetailedMessage);
                    break;
                default:
                    Assert.ThrowsAsync<RpcFailureException>(call);
                    break;
            }
        }

        private static async IAsyncEnumerable<SequenceData> GenerateSequence(int count)
        {
            for (int i = 0; i < count; i++)
            {
                await Task.Yield();
                yield return new SequenceData(i + 1);
            }
        }

        private async Task RunWithServiceAsync(StreamingFaultKind faultKind, Func<IStreamingFaultService, Task> test)
        {
            var builder = new RpcServiceDefinitionsBuilder();
            builder.RegisterService<IStreamingFaultService>();

            // The converter is only registered for converted exceptions. On the client, it would also convert the declared
            // fault, since it has the same fault code.
            bool useConverter = faultKind == StreamingFaultKind.Converted;
            var (server, connection) = this.CreateServerAndConnection(builder,
                so => { if (useConverter) so.ExceptionConverters.Add(new DeclaredFaultExceptionConverter()); },
                co => { if (useConverter) co.ExceptionConverters.Add(new DeclaredFaultExceptionConverter()); });
            using var publishedService = server.ServicePublisher.PublishSingleton<IStreamingFaultService>(new StreamingFaultServiceImpl());
            try
            {
                server.Start();

                await test(connection.GetServiceSingleton<IStreamingFaultService>());
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }
    }
}
