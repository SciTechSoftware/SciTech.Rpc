using NUnit.Framework;
using SciTech.Rpc.Serialization;
using System;

namespace SciTech.Rpc.Tests
{
    [TestFixtureSource(nameof(Serializers))]
    public class ObjectRefSerializationTests
    {
        private static readonly object[] Serializers =
        {
            new JsonRpcSerializer(),
            new ProtobufRpcSerializer(),
            new DataContractRpcSerializer(),
        };

        private static readonly RpcConnectionInfo ConnectionInfo = new RpcConnectionInfo("Server", new Uri("lightweight.tcp://localhost:1234"), RpcServerId.NewId());

        private readonly IRpcSerializer serializer;

        public ObjectRefSerializationTests(IRpcSerializer serializer)
        {
            this.serializer = serializer;
        }

        [Test]
        public void TypedObjectRef_RoundTrips()
        {
            var objectRef = new RpcObjectRef<ISimpleService>(ConnectionInfo, RpcObjectId.NewId(), new[] { "Service" });

            var typedSerializer = this.serializer.CreateTyped<RpcObjectRef<ISimpleService>>();
            var deserializedRef = typedSerializer.Deserialize(typedSerializer.Serialize(objectRef));

            Assert.AreEqual(objectRef.ObjectId, deserializedRef.ObjectId);
            Assert.AreEqual(ConnectionInfo, deserializedRef.ServerConnection);
            CollectionAssert.AreEqual(objectRef.ImplementedServices, deserializedRef.ImplementedServices);
        }

        [Test]
        public void ObjectRef_RoundTrips()
        {
            var objectRef = new RpcObjectRef(ConnectionInfo, RpcObjectId.NewId(), new[] { "Service" });

            var typedSerializer = this.serializer.CreateTyped<RpcObjectRef>();
            var deserializedRef = typedSerializer.Deserialize(typedSerializer.Serialize(objectRef));

            Assert.AreEqual(objectRef, deserializedRef);
            CollectionAssert.AreEqual(objectRef.ImplementedServices, deserializedRef.ImplementedServices);
        }

        [Test]
        public void SingletonRef_RoundTrips()
        {
            var singletonRef = new RpcSingletonRef<ISimpleService>(ConnectionInfo);

            var typedSerializer = this.serializer.CreateTyped<RpcSingletonRef<ISimpleService>>();
            var deserializedRef = typedSerializer.Deserialize(typedSerializer.Serialize(singletonRef));

            Assert.AreEqual(ConnectionInfo, deserializedRef.ServerConnection);
        }
    }
}
