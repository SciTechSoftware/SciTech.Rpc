using System;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using NUnit.Framework;

namespace SciTech.Rpc.Tests
{
    /// <summary>
    /// These tests are mainly intended to test assumptions about the DataContractSerializer.
    /// </summary>
    public class DataContractSerializerTests
    {
        [SetUp]
        public void Setup()
        {
        }

        private void ToStream(Stream stream, bool binary, object input)
        {
            XmlDictionaryWriter writer;
            if( binary )
            {
                writer = XmlDictionaryWriter.CreateBinaryWriter(stream, null, null, false);
            } else
            {
                writer = XmlDictionaryWriter.CreateDictionaryWriter(XmlWriter.Create(stream, new XmlWriterSettings { Indent = true } ));
            }

            using (writer)
            {
                var type = input.GetType();
                var serializer = new DataContractSerializer(type); // settings != null ? new DataContractSerializer(type, settings) : new DataContractSerializer(type);
                serializer.WriteObject(writer, input);
            }
        }

        public static object FromStream(Type type, bool binary, Stream input)
        {
            XmlDictionaryReader reader;
            if (binary)
            {
                reader = XmlDictionaryReader.CreateBinaryReader(input, XmlDictionaryReaderQuotas.Max);
            }
            else
            {
                reader = XmlDictionaryReader.CreateDictionaryReader(XmlReader.Create(input, new XmlReaderSettings()));
            }

            var serializer = new DataContractSerializer(type);
            using (reader)
            {
                var value = serializer.ReadObject(reader);
                return value;
            }
        }


        [Test]
        public void RefObjOfTypeToRefObjTest()
        {
            var ms = new MemoryStream();
            ToStream(ms, false, new RpcObjectRef<ISimpleService>(new RpcServerConnectionInfo("Test", null, RpcServerId.NewId()), RpcObjectId.NewId(), null));
            string text = Encoding.UTF8.GetString(ms.GetBuffer());

            var ms2 = new MemoryStream();
            ToStream(ms2, true, new RpcObjectRef<ISimpleService>(new RpcServerConnectionInfo("Test", null, RpcServerId.NewId()), RpcObjectId.NewId(), null));

            ms.Seek(0, SeekOrigin.Begin);
            var ref1 = FromStream(typeof(RpcObjectRef<IBlockingService>), false, ms);

            ms2.Seek(0, SeekOrigin.Begin);
            var ref2 = FromStream(typeof(RpcObjectRef<IBlockingService>), true, ms2);


            Assert.IsTrue(text.Length > 0);
        }

        //[Test]
        //public void SimpleNullableIntResponseTest()
        //{
        //    for (int i = 0; i < 100; i++)
        //    {
        //        RpcResponse<int?> r1;
        //        if (i == 0)
        //        {
        //            r1 = new RpcResponse<int?> { Result = null, Error = new RpcError { Message = "Test error" } };
        //        }
        //        else
        //        {
        //            r1 = new RpcResponse<int?> { Result = 1 + i * 12345 };

        //        }


        //        var ms = new MemoryStream();

        //        Serializer.Serialize(ms, r1);
        //        ms.Seek(0, SeekOrigin.Begin);

        //        var dr1 = Serializer.Deserialize<RpcResponse<int?>>(ms);

        //        Assert.AreEqual(r1.Result, dr1.Result);
        //        Assert.AreEqual(r1.Error?.Message, dr1.Error?.Message);
        //    }
        //}

        //[Test]
        //public void VoidRequestTest()
        //{
        //    var objectId = RpcObjectId.NewId();
        //    RpcObjectRequest request = new RpcObjectRequest(objectId);

        //    var ms = new MemoryStream();

        //    Serializer.Serialize(ms, request);
        //    ms.Seek(0, SeekOrigin.Begin);

        //    var dr = Serializer.Deserialize<RpcObjectRequest>(ms);

        //    Assert.AreEqual(request.Id, dr.Id);
        //}

        //[Test]
        //public void OneParamRequestTest()
        //{
        //    var objectId = RpcObjectId.NewId();
        //    RpcObjectRequest<double> request = new RpcObjectRequest<double>(objectId, 18);

        //    var ms = new MemoryStream();

        //    Serializer.Serialize(ms, request);
        //    ms.Seek(0, SeekOrigin.Begin);

        //    var dr = Serializer.Deserialize<RpcObjectRequest<double>>(ms);

        //    Assert.AreEqual(request.Id, dr.Id);
        //    Assert.AreEqual(request.Value1, dr.Value1);
        //}


        //[Test]
        //public void MultiParamRequestTest()
        //{
        //    var objectId = RpcObjectId.NewId();
        //    var request = new RpcObjectRequest<double, int>(objectId, 18.12, 9991);

        //    var ms = new MemoryStream();

        //    Serializer.Serialize(ms, request);
        //    ms.Seek(0, SeekOrigin.Begin);

        //    var dr = Serializer.Deserialize<RpcObjectRequest<double, int>>(ms);

        //    Assert.AreEqual(request.Id, dr.Id);
        //    Assert.AreEqual(request.Value1, dr.Value1);
        //    Assert.AreEqual(request.Value2, dr.Value2);

        //    var request2 = new RpcObjectRequest<double, int, string>(objectId, 18.12, 9991, "A string");

        //    ms.Seek(0, SeekOrigin.Begin);
        //    Serializer.Serialize(ms, request2);
        //    ms.Seek(0, SeekOrigin.Begin);

        //    var dr2 = Serializer.Deserialize<RpcObjectRequest<double, int, string>>(ms);

        //    Assert.AreEqual(request2.Id, dr2.Id);
        //    Assert.AreEqual(request2.Value1, dr2.Value1);
        //    Assert.AreEqual(request2.Value2, dr2.Value2);
        //    Assert.AreEqual(request2.Value3, dr2.Value3);

        //}

        //[Test]
        //public void EmptyDerivedTest()
        //{
        //    for (int i = 0; i < 100; i++)
        //    {
        //        var d = new EmptyDerivedClass { a = i, b = (uint)i * 2 };

        //        var ms = new MemoryStream();

        //        Serializer.Serialize(ms, d);
        //        ms.Seek(0, SeekOrigin.Begin);

        //        var dr = Serializer.Deserialize<EmptyDerivedClass>(ms);

        //        Assert.AreEqual(d.a, dr.a);
        //        Assert.AreEqual(d.b, dr.b);
        //    }
        //}


        //[Test]
        //public void DerivedTest()
        //{
        //    for (int i = 0; i < 100; i++)
        //    {
        //        var d = new DerivedClass { a = i, b = (uint)i * 2, c = (uint)i * 3 };

        //        var ms = new MemoryStream();

        //        Serializer.Serialize(ms, d);
        //        ms.Seek(0, SeekOrigin.Begin);

        //        var dr = Serializer.Deserialize<DerivedClass>(ms);

        //        Assert.AreEqual(d.a, dr.a);
        //        Assert.AreEqual(d.b, dr.b);
        //        Assert.AreEqual(d.c, dr.c);
        //    }
        //}
    }

    //[ProtoContract]
    //public class BaseClass
    //{
    //    [ProtoMember(1)]
    //    internal int a;
    //    [ProtoMember(2)]
    //    internal uint b;
    //}

    //[ProtoContract]
    //public class EmptyDerivedClass : BaseClass
    //{
    //    [ProtoMember(1)]
    //    internal new int a { get => base.a; set=> base.a    = value;} 

    //    [ProtoMember(2)]
    //    internal new uint b { get => base.b; set => base.b = value; }
    //}

    //[ProtoContract]
    //public class DerivedClass : BaseClass
    //{
    //    [ProtoMember(1)]
    //    internal new int a { get => base.a; set => base.a = value; }

    //    [ProtoMember(2)]
    //    internal new uint b { get => base.b; set => base.b = value; }

    //    [ProtoMember(3)]
    //    internal uint c;
    //}
}