using NUnit.Framework;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Serialization.Internal;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SciTech.Rpc.Tests
{
    [DataContract]
    [RpcKnownType(typeof(KnownTypeCircle), 10)]
    [RpcKnownType(typeof(KnownTypeSquare), 11, Discriminator = "Sq")]
    public class KnownTypeShape
    {
        [DataMember(Order = 1)]
        public string Name { get; set; }
    }

    [DataContract]
    public class KnownTypeCircle : KnownTypeShape
    {
        [DataMember(Order = 1)]
        public double Radius { get; set; }
    }

    [DataContract]
    public class KnownTypeSquare : KnownTypeShape
    {
        [DataMember(Order = 1)]
        public double Side { get; set; }
    }

    [DataContract]
    public class KnownTypeDrawing
    {
        [DataMember(Order = 1)]
        public KnownTypeShape Shape { get; set; }

        [DataMember(Order = 2)]
        public List<KnownTypeShape> Shapes { get; set; }
    }

    [DataContract]
    [RpcKnownType(typeof(KnownTypeShape), 1)]
    public class InvalidKnownTypeDeclaration
    {
    }

    [TestFixtureSource(nameof(Serializers))]
    public class KnownTypeSerializationTests
    {
        private static readonly object[] Serializers =
        {
            new JsonRpcSerializer(),
            new ProtobufRpcSerializer(),
            new DataContractRpcSerializer(),
        };

        private readonly IRpcSerializer serializer;

        public KnownTypeSerializationTests(IRpcSerializer serializer)
        {
            this.serializer = serializer;
        }

        [Test]
        public void DerivedType_AsDeclaredBaseType_RoundTrips()
        {
            var typedSerializer = this.serializer.CreateTyped<KnownTypeShape>();

            var circle = (KnownTypeCircle)typedSerializer.Deserialize(typedSerializer.Serialize(new KnownTypeCircle { Name = "c", Radius = 1.5 }));
            Assert.AreEqual("c", circle.Name);
            Assert.AreEqual(1.5, circle.Radius);

            var square = (KnownTypeSquare)typedSerializer.Deserialize(typedSerializer.Serialize(new KnownTypeSquare { Name = "s", Side = 2 }));
            Assert.AreEqual("s", square.Name);
            Assert.AreEqual(2, square.Side);
        }

        [Test]
        public void DerivedType_AsDeclaredBaseType_Untyped_RoundTrips()
        {
            var data = this.serializer.Serialize(new KnownTypeCircle { Name = "c", Radius = 1.5 }, typeof(KnownTypeShape));
            var circle = (KnownTypeCircle)this.serializer.Deserialize(data, typeof(KnownTypeShape));

            Assert.AreEqual("c", circle.Name);
            Assert.AreEqual(1.5, circle.Radius);
        }

        [Test]
        public void DerivedType_AsDeclaredType_KeepsBaseMembers()
        {
            // Protobuf drops the base members of a derived type unless it is registered as a sub-type.
            var typedSerializer = this.serializer.CreateTyped<KnownTypeCircle>();

            var circle = typedSerializer.Deserialize(typedSerializer.Serialize(new KnownTypeCircle { Name = "c", Radius = 1.5 }));

            Assert.AreEqual("c", circle.Name);
            Assert.AreEqual(1.5, circle.Radius);
        }

        [Test]
        public void BaseType_RoundTrips()
        {
            var typedSerializer = this.serializer.CreateTyped<KnownTypeShape>();

            var shape = typedSerializer.Deserialize(typedSerializer.Serialize(new KnownTypeShape { Name = "b" }));

            Assert.AreEqual(typeof(KnownTypeShape), shape.GetType());
            Assert.AreEqual("b", shape.Name);
        }

        [Test]
        public void DerivedType_AsMember_RoundTrips()
        {
            var typedSerializer = this.serializer.CreateTyped<KnownTypeDrawing>();

            var drawing = typedSerializer.Deserialize(typedSerializer.Serialize(new KnownTypeDrawing
            {
                Shape = new KnownTypeSquare { Name = "s", Side = 2 },
                Shapes = new List<KnownTypeShape> { new KnownTypeCircle { Name = "c", Radius = 1.5 }, new KnownTypeShape { Name = "b" } }
            }));

            Assert.AreEqual(2, ((KnownTypeSquare)drawing.Shape).Side);
            Assert.AreEqual(2, drawing.Shapes.Count);
            Assert.AreEqual(1.5, ((KnownTypeCircle)drawing.Shapes[0]).Radius);
            Assert.AreEqual(typeof(KnownTypeShape), drawing.Shapes[1].GetType());
            Assert.AreEqual("b", drawing.Shapes[1].Name);
        }
    }

    public class RpcKnownTypesTests
    {
        [Test]
        public void KnownTypes_AreOrdered()
        {
            var knownTypes = RpcKnownTypes.GetDeclaredKnownTypes(typeof(KnownTypeShape));

            Assert.AreEqual(2, knownTypes.Count);
            Assert.AreEqual(typeof(KnownTypeCircle), knownTypes[0].KnownType);
            Assert.AreEqual("KnownTypeCircle", knownTypes[0].Discriminator);
            Assert.AreEqual(typeof(KnownTypeSquare), knownTypes[1].KnownType);
            Assert.AreEqual("Sq", knownTypes[1].Discriminator);
        }

        [Test]
        public void FindKnownType_SearchesBaseTypes()
        {
            Assert.AreEqual(typeof(KnownTypeSquare), RpcKnownTypes.FindKnownType(typeof(KnownTypeShape), "Sq")?.KnownType);
            Assert.IsNull(RpcKnownTypes.FindKnownType(typeof(KnownTypeCircle), "Sq"), "Square is not assignable to Circle.");
            Assert.IsNull(RpcKnownTypes.FindKnownType(typeof(KnownTypeDrawing), typeof(KnownTypeSquare)));
        }

        [Test]
        public void KnownType_NotDerived_Throws()
        {
            Assert.Throws<RpcDefinitionException>(() => RpcKnownTypes.GetDeclaredKnownTypes(typeof(InvalidKnownTypeDeclaration)));
        }

        [Test]
        public void KnownType_InvalidOrder_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RpcKnownTypeAttribute(typeof(KnownTypeCircle), 0));
        }
    }
}
