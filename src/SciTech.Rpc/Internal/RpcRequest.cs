using System;
using System.Linq;
using System.Runtime.Serialization;

namespace SciTech.Rpc.Internal
{
#pragma warning disable CA1051 // Do not declare visible instance fields
#nullable disable
    [DataContract]
    public sealed class RpcRequest : IObjectRequest
    {
        public RpcRequest()
        {
        }

        RpcObjectId IObjectRequest.Id => RpcObjectId.Empty;
    }

    [DataContract]
    public sealed class RpcRequest<T1> : IObjectRequest
    {

        [DataMember(Order = 2)]
        public T1 Value1;

        public RpcRequest()
        {

        }

        public RpcRequest(T1 value1)
        {
            this.Value1 = value1;
        }

        RpcObjectId IObjectRequest.Id => RpcObjectId.Empty;
    }

    [DataContract]
    public sealed class RpcRequest<T1, T2> : IObjectRequest
    {
        [DataMember(Order = 2)]
        public T1 Value1;

        [DataMember(Order = 3)]
        public T2 Value2;

        public RpcRequest()
        {

        }

        public RpcRequest(T1 value1, T2 value2)
        {
            this.Value1 = value1;
            this.Value2 = value2;
        }

        RpcObjectId IObjectRequest.Id => RpcObjectId.Empty;
    }

    [DataContract]
    public sealed class RpcRequest<T1, T2, T3> : IObjectRequest
    {
        [DataMember(Order = 2)]
        public T1 Value1;

        [DataMember(Order = 3)]
        public T2 Value2;

        [DataMember(Order = 4)]
        public T3 Value3;

        public RpcRequest()
        {

        }

        public RpcRequest(T1 value1, T2 value2, T3 value3)
        {
            this.Value1 = value1;
            this.Value2 = value2;
            this.Value3 = value3;
        }

        RpcObjectId IObjectRequest.Id => RpcObjectId.Empty;
    }

    [DataContract]
    public sealed class RpcRequest<T1, T2, T3, T4> : IObjectRequest
    {
        [DataMember(Order = 2)]
        public T1 Value1;

        [DataMember(Order = 3)]
        public T2 Value2;

        [DataMember(Order = 4)]
        public T3 Value3;

        [DataMember(Order = 5)]
        public T4 Value4;

        public RpcRequest()
        {

        }

        public RpcRequest(T1 value1, T2 value2, T3 value3, T4 value4)
        {
            this.Value1 = value1;
            this.Value2 = value2;
            this.Value3 = value3;
            this.Value4 = value4;
        }

        RpcObjectId IObjectRequest.Id => RpcObjectId.Empty;
    }

    [DataContract]
    public sealed class RpcRequest<T1, T2, T3, T4, T5> : IObjectRequest
    {
        [DataMember(Order = 2)]
        public T1 Value1;

        [DataMember(Order = 3)]
        public T2 Value2;

        [DataMember(Order = 4)]
        public T3 Value3;

        [DataMember(Order = 5)]
        public T4 Value4;

        [DataMember(Order = 6)]
        public T5 Value5;

        public RpcRequest()
        {

        }

        public RpcRequest(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5)
        {
            this.Value1 = value1;
            this.Value2 = value2;
            this.Value3 = value3;
            this.Value4 = value4;
            this.Value5 = value5;
        }

        RpcObjectId IObjectRequest.Id => RpcObjectId.Empty;
    }

    [DataContract]
    public sealed class RpcRequest<T1, T2, T3, T4, T5, T6> : IObjectRequest
    {
        [DataMember(Order = 2)]
        public T1 Value1;

        [DataMember(Order = 3)]
        public T2 Value2;

        [DataMember(Order = 4)]
        public T3 Value3;

        [DataMember(Order = 5)]
        public T4 Value4;

        [DataMember(Order = 6)]
        public T5 Value5;

        [DataMember(Order = 7)]
        public T6 Value6;

        public RpcRequest()
        {

        }

        public RpcRequest(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6)
        {
            this.Value1 = value1;
            this.Value2 = value2;
            this.Value3 = value3;
            this.Value4 = value4;
            this.Value5 = value5;
            this.Value6 = value6;
        }

        RpcObjectId IObjectRequest.Id => RpcObjectId.Empty;
    }

    [DataContract]
    public sealed class RpcRequest<T1, T2, T3, T4, T5, T6, T7> : IObjectRequest
    {
        [DataMember(Order = 2)]
        public T1 Value1;

        [DataMember(Order = 3)]
        public T2 Value2;

        [DataMember(Order = 4)]
        public T3 Value3;

        [DataMember(Order = 5)]
        public T4 Value4;

        [DataMember(Order = 6)]
        public T5 Value5;

        [DataMember(Order = 7)]
        public T6 Value6;

        [DataMember(Order = 8)]
        public T7 Value7;

        public RpcRequest()
        {

        }

        public RpcRequest(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7)
        {
            this.Value1 = value1;
            this.Value2 = value2;
            this.Value3 = value3;
            this.Value4 = value4;
            this.Value5 = value5;
            this.Value6 = value6;
            this.Value7 = value7;
        }

        RpcObjectId IObjectRequest.Id => RpcObjectId.Empty;
    }

    [DataContract]
    public sealed class RpcRequest<T1, T2, T3, T4, T5, T6, T7, T8> : IObjectRequest
    {
        [DataMember(Order = 2)]
        public T1 Value1;

        [DataMember(Order = 3)]
        public T2 Value2;

        [DataMember(Order = 4)]
        public T3 Value3;

        [DataMember(Order = 5)]
        public T4 Value4;

        [DataMember(Order = 6)]
        public T5 Value5;

        [DataMember(Order = 7)]
        public T6 Value6;

        [DataMember(Order = 8)]
        public T7 Value7;

        [DataMember(Order = 9)]
        public T8 Value8;

        public RpcRequest()
        {

        }

        public RpcRequest(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8)
        {
            this.Value1 = value1;
            this.Value2 = value2;
            this.Value3 = value3;
            this.Value4 = value4;
            this.Value5 = value5;
            this.Value6 = value6;
            this.Value7 = value7;
            this.Value8 = value8;
        }

        RpcObjectId IObjectRequest.Id => RpcObjectId.Empty;
    }

    [DataContract]
    public sealed class RpcRequest<T1, T2, T3, T4, T5, T6, T7, T8, T9> : IObjectRequest
    {
        [DataMember(Order = 2)]
        public T1 Value1;

        [DataMember(Order = 3)]
        public T2 Value2;

        [DataMember(Order = 4)]
        public T3 Value3;

        [DataMember(Order = 5)]
        public T4 Value4;

        [DataMember(Order = 6)]
        public T5 Value5;

        [DataMember(Order = 7)]
        public T6 Value6;

        [DataMember(Order = 8)]
        public T7 Value7;

        [DataMember(Order = 9)]
        public T8 Value8;

        [DataMember(Order = 10)]
        public T9 Value9;

        public RpcRequest()
        {

        }

        public RpcRequest(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9)
        {
            this.Value1 = value1;
            this.Value2 = value2;
            this.Value3 = value3;
            this.Value4 = value4;
            this.Value5 = value5;
            this.Value6 = value6;
            this.Value7 = value7;
            this.Value8 = value8;
            this.Value9 = value9;
        }

        RpcObjectId IObjectRequest.Id => RpcObjectId.Empty;
    }
#nullable restore
#pragma warning restore CA1051 // Do not declare visible instance fields
}
