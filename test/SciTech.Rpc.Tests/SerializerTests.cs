using NUnit.Framework;
using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests
{

    public class SerializerTests
    {          
        [Test]
        public void SimpleSerializationTest()
        {

        }
    }

    //[DataContract]
    //public class PolymorphicMemberClass
    //{
    //    public PolymorphicMemberClass(BaseClass derived, BaseClass empty)
    //    {
    //        this.Derived = derived;
    //        this.Empty = empty;
    //    }

    //    public BaseClass Derived { get; set; }

    //    public BaseClass Empty { get; set; }



    //}

    //[DataContract]
    //public class BaseClass
    //{
    //    [DataMember(Order = 1)]
    //    internal int a;

    //    [DataMember(Order = 2)]
    //    internal uint b;
    //}

    //[DataContract]
    //public class ClassWithBool
    //{
    //    [DataMember(Order = 1)]
    //    public bool First;

    //    [DataMember(Order = 2)]
    //    public bool Second;
    //}

    //[DataContract]
    //public class DerivedClass : BaseClass
    //{

    //    [DataMember(Order = 3)]
    //    internal uint c;
    //}

    //[DataContract]
    //public class EmptyDerivedClass : BaseClass
    //{
    //}
}
