using System.IO;
using NUnit.Framework;
using ProtoBuf;
using SciTech.Rpc.Grpc;
using SciTech.Rpc.Internal;

namespace SciTech.Rpc
{
    /// <summary>
    /// These tests are mainly intended to test assumptions about the Protobuf serializer.
    /// </summary>
    public class ProtobufTests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void SimpleIntResponseTest()
        {
            for (int i = 0; i < 100; i++)
            {
                var r1 = new RpcResponse<int> { Result = 1 + i * 12345 };

                var ms = new MemoryStream();

                Serializer.Serialize(ms, r1);
                ms.Seek(0, SeekOrigin.Begin);

                var dr1 = Serializer.Deserialize<RpcResponse<int>>(ms);

                Assert.AreEqual(r1.Result, dr1.Result);
                Assert.AreEqual(r1.Error, dr1.Error);
            }
        }

        [Test]
        public void NullResponseTest()
        {
            RpcResponse<string> r1 = null;

            var ms = new MemoryStream();
            Serializer.Serialize<RpcResponse<string>>(ms, r1);

            ms.Seek(0, SeekOrigin.Begin);

            var dr1 = Serializer.Deserialize<RpcResponse<string>>(ms);
            Assert.IsNotNull(dr1);
            Assert.IsNull(dr1.Error);
            Assert.IsNull(dr1.Result);
        }

        [Test]
        public void EmptyStringResponseTest()
        {
            RpcResponse<string> r1 = new RpcResponse<string> { Result = "" };

            var ms = new MemoryStream();
            Serializer.Serialize<RpcResponse<string>>(ms, r1);

            ms.Seek(0, SeekOrigin.Begin);

            var dr1 = Serializer.Deserialize<RpcResponse<string>>(ms);
            Assert.IsNotNull(dr1);
            Assert.IsNull(dr1.Error);
            Assert.IsEmpty(dr1.Result);
        }

        [Test]
        public void NullStringResponseTest()
        {
            RpcResponse<string> r1 = new RpcResponse<string>();

            var ms = new MemoryStream();
            Serializer.Serialize<RpcResponse<string>>(ms, r1);

            ms.Seek(0, SeekOrigin.Begin);

            var dr1 = Serializer.Deserialize<RpcResponse<string>>(ms);
            Assert.IsNotNull(dr1);
            Assert.IsNull(dr1.Error);
            Assert.IsNull(dr1.Result);
        }

        [Test]
        public void SimpleNullableIntResponseTest()
        {
            for (int i = 0; i < 100; i++)
            {
                RpcResponse<int?> r1;
                if (i == 0)
                {
                    r1 = new RpcResponse<int?> { Result = null, Error = new RpcError { Message = "Test error" } };
                }
                else
                {
                    r1 = new RpcResponse<int?> { Result = 1 + i * 12345 };

                }


                var ms = new MemoryStream();

                Serializer.Serialize(ms, r1);
                ms.Seek(0, SeekOrigin.Begin);

                var dr1 = Serializer.Deserialize<RpcResponse<int?>>(ms);

                Assert.AreEqual(r1.Result, dr1.Result);
                Assert.AreEqual(r1.Error?.Message, dr1.Error?.Message);
            }
        }

        [Test]
        public void VoidRequestTest()
        {
            var objectId = RpcObjectId.NewId();
            RpcObjectRequest request = new RpcObjectRequest(objectId);

            var ms = new MemoryStream();

            Serializer.Serialize(ms, request);
            ms.Seek(0, SeekOrigin.Begin);

            var dr = Serializer.Deserialize<RpcObjectRequest>(ms);

            Assert.AreEqual(request.Id, dr.Id);
        }

        [Test]
        public void OneParamRequestTest()
        {
            var objectId = RpcObjectId.NewId();
            RpcObjectRequest<double> request = new RpcObjectRequest<double>(objectId, 18);

            var ms = new MemoryStream();

            Serializer.Serialize(ms, request);
            ms.Seek(0, SeekOrigin.Begin);

            var dr = Serializer.Deserialize<RpcObjectRequest<double>>(ms);

            Assert.AreEqual(request.Id, dr.Id);
            Assert.AreEqual(request.Value1, dr.Value1);
        }


        [Test]
        public void MultiParamRequestTest()
        {
            var objectId = RpcObjectId.NewId();
            var request = new RpcObjectRequest<double, int>(objectId, 18.12, 9991);

            var ms = new MemoryStream();

            Serializer.Serialize(ms, request);
            ms.Seek(0, SeekOrigin.Begin);

            var dr = Serializer.Deserialize<RpcObjectRequest<double, int>>(ms);

            Assert.AreEqual(request.Id, dr.Id);
            Assert.AreEqual(request.Value1, dr.Value1);
            Assert.AreEqual(request.Value2, dr.Value2);

            var request2 = new RpcObjectRequest<double, int, string>(objectId, 18.12, 9991, "A string");

            ms.Seek(0, SeekOrigin.Begin);
            Serializer.Serialize(ms, request2);
            ms.Seek(0, SeekOrigin.Begin);

            var dr2 = Serializer.Deserialize<RpcObjectRequest<double, int, string>>(ms);

            Assert.AreEqual(request2.Id, dr2.Id);
            Assert.AreEqual(request2.Value1, dr2.Value1);
            Assert.AreEqual(request2.Value2, dr2.Value2);
            Assert.AreEqual(request2.Value3, dr2.Value3);

        }

        [Test]
        public void EmptyDerivedTest()
        {
            for (int i = 0; i < 100; i++)
            {
                var d = new EmptyDerivedClass { a = i, b = (uint)i * 2 };

                var ms = new MemoryStream();

                Serializer.Serialize(ms, d);
                ms.Seek(0, SeekOrigin.Begin);

                var dr = Serializer.Deserialize<EmptyDerivedClass>(ms);

                Assert.AreEqual(d.a, dr.a);
                Assert.AreEqual(d.b, dr.b);
            }
        }


        [Test]
        public void DerivedTest()
        {
            for (int i = 0; i < 100; i++)
            {
                var d = new DerivedClass { a = i, b = (uint)i * 2, c = (uint)i * 3 };

                var ms = new MemoryStream();

                Serializer.Serialize(ms, d);
                ms.Seek(0, SeekOrigin.Begin);

                var dr = Serializer.Deserialize<DerivedClass>(ms);

                Assert.AreEqual(d.a, dr.a);
                Assert.AreEqual(d.b, dr.b);
                Assert.AreEqual(d.c, dr.c);
            }
        }

        [Test(Description ="Test of error in Protobuf 3.0.0-alpha.32")]
        public void BoolRequestErrorTest()

        {
            var request = new ClassWithBool { First = true, Second = false };
            var ms = new MemoryStream();

            Serializer.Serialize(ms, request);
            ms.Seek(0, SeekOrigin.Begin);

            // Deserialize will throw in Protobuf 3.0.0-alpha.32
            var dr = Serializer.Deserialize<ClassWithBool>(ms);
            Assert.AreEqual(request.First, dr.First);
            Assert.AreEqual(request.Second, dr.Second);
        }
    }

    [ProtoContract]
    public class ClassWithBool
    {
        [ProtoMember(1)]
        public bool First;

        [ProtoMember(2)]
        public bool Second;
    }

    [ProtoContract]
    public class BaseClass
    {
        [ProtoMember(1)]
        internal int a;
        [ProtoMember(2)]
        internal uint b;
    }

    [ProtoContract]
    public class EmptyDerivedClass : BaseClass
    {
        [ProtoMember(1)]
        internal new int a { get => base.a; set=> base.a    = value;} 

        [ProtoMember(2)]
        internal new uint b { get => base.b; set => base.b = value; }
    }

    [ProtoContract]
    public class DerivedClass : BaseClass
    {
        [ProtoMember(1)]
        internal new int a { get => base.a; set => base.a = value; }

        [ProtoMember(2)]
        internal new uint b { get => base.b; set => base.b = value; }

        [ProtoMember(3)]
        internal uint c;
    }
}