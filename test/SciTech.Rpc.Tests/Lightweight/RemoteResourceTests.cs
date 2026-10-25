using NUnit.Framework;
using SciTech.Rpc.Client;
using SciTech.Rpc.Resources;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server;
using System;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Lightweight
{
    [RpcService]
    public interface IResourceTestService
    {
        AcquiredRemoteLock AcquireWriterLock();

        /// <summary>
        /// Returns an <see cref="AcquiredRemoteLock"/> declared as its base type, to test the known type.
        /// </summary>
        AcquiredRemoteResource AcquireReaderLockAsResource();

        AcquiredService<IResourceTestClientService> CreateClientService(int value);

        ReaderWriterLockState GetLockState();
    }

    [RpcService]
    public interface IResourceTestClientService
    {
        int GetValue();
    }

    public sealed class ResourceTestServiceImpl : IResourceTestService, IDisposable
    {
        private readonly RemoteResourceLifetimeManager lifetimeManager = new RemoteResourceLifetimeManager();

        private readonly RemoteReaderWriterLock readerWriterLock;

        private readonly IRpcServicePublisher publisher;

        public ResourceTestServiceImpl(IRpcServicePublisher publisher)
        {
            this.publisher = publisher;
            this.lifetimeManager.Publish(publisher);
            this.readerWriterLock = new RemoteReaderWriterLock(this.lifetimeManager);
        }

        public AcquiredRemoteLock AcquireWriterLock() => this.readerWriterLock.AcquireWriterLock();

        public AcquiredRemoteResource AcquireReaderLockAsResource() => this.readerWriterLock.AcquireReaderLock();

        public AcquiredService<IResourceTestClientService> CreateClientService(int value)
            => this.publisher.PublishClientService<IResourceTestClientService>(new ResourceTestClientServiceImpl(value), this.lifetimeManager);

        public ReaderWriterLockState GetLockState() => this.readerWriterLock.LockState;

        public void Dispose() => this.lifetimeManager.Dispose();
    }

    public sealed class ResourceTestClientServiceImpl : IResourceTestClientService
    {
        private readonly int value;

        public ResourceTestClientServiceImpl(int value) => this.value = value;

        public int GetValue() => this.value;
    }

    [TestFixtureSource(nameof(FixtureArgs))]
    public class RemoteResourceTests : ClientServerTestsBase
    {
        private static readonly object[] FixtureArgs =
        {
            new object[] { new JsonRpcSerializer(), RpcConnectionType.LightweightInproc },
            new object[] { new JsonRpcSerializer(), RpcConnectionType.LightweightTcp },
            new object[] { new ProtobufRpcSerializer(), RpcConnectionType.LightweightInproc },
            new object[] { new ProtobufRpcSerializer(), RpcConnectionType.LightweightTcp },
            new object[] { new DataContractRpcSerializer(), RpcConnectionType.LightweightInproc },
            new object[] { new DataContractRpcSerializer(), RpcConnectionType.LightweightTcp },
        };

        public RemoteResourceTests(IRpcSerializer serializer, RpcConnectionType connectionType)
            : base(serializer, connectionType)
        {
        }

        [Test]
        public async Task AcquiredLock_TakeOwnershipAndRelease_UpdatesServerLock()
        {
            await this.RunAsync(async (service, connection) =>
            {
                var acquiredLock = service.AcquireWriterLock();
                Assert.NotNull(acquiredLock);
                Assert.IsTrue(await acquiredLock.TakeOwnershipAsync(connection));
                Assert.AreEqual(ReaderWriterLockState.WriteLock, service.GetLockState());

                // The lock is held, so it cannot be acquired again.
                Assert.IsNull(service.AcquireWriterLock());

                await acquiredLock.ReleaseAsync();
                Assert.AreEqual(ReaderWriterLockState.NoLock, service.GetLockState());

                var reacquiredLock = service.AcquireWriterLock();
                Assert.NotNull(reacquiredLock);
                Assert.IsTrue(await reacquiredLock.TakeOwnershipAsync(connection));
                await reacquiredLock.ReleaseAsync();
            });
        }

        [Test]
        public async Task AcquiredLock_AsBaseType_KeepsDerivedType()
        {
            await this.RunAsync(async (service, connection) =>
            {
                var acquiredResource = service.AcquireReaderLockAsResource();

                Assert.IsInstanceOf<AcquiredRemoteLock>(acquiredResource);
                Assert.IsTrue(await acquiredResource.TakeOwnershipAsync(connection));
                Assert.AreEqual(ReaderWriterLockState.ReadLock, service.GetLockState());

                await acquiredResource.ReleaseAsync();
                Assert.AreEqual(ReaderWriterLockState.NoLock, service.GetLockState());
            });
        }

        [Test]
        public async Task AcquiredService_TakeOwnershipAndRelease_UnpublishesService()
        {
            await this.RunAsync(async (service, connection) =>
            {
                var acquiredService = service.CreateClientService(42);
                Assert.NotNull(acquiredService);

                var clientService = await acquiredService.TakeOwnershipAsync(connection, false);
                Assert.NotNull(clientService);
                Assert.AreSame(clientService, acquiredService.Value);
                Assert.AreEqual(42, clientService.GetValue());

                await acquiredService.ReleaseAsync();
                Assert.IsNull(acquiredService.Value);
                Assert.Throws<RpcServiceUnavailableException>(() => clientService.GetValue());
            });
        }

        private async Task RunAsync(Func<IResourceTestService, IRpcChannel, Task> test)
        {
            var definitionsBuilder = new RpcServiceDefinitionsBuilder();
            definitionsBuilder
                .RegisterService<IResourceTestService>()
                .RegisterService<IResourceTestClientService>()
                .RegisterService<IRemoteResourceLifetimeService>();

            var (server, connection) = this.CreateServerAndConnection(definitionsBuilder);
            using var serviceImpl = new ResourceTestServiceImpl(server.ServicePublisher);
            using var publishedService = server.ServicePublisher.PublishSingleton<IResourceTestService>(serviceImpl);
            server.Start();
            try
            {
                var service = connection.GetServiceSingleton<IResourceTestService>();
                await test(service, connection).DefaultTimeout();
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }
    }
}
