#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using System;
using System.Linq;
using System.Runtime.Serialization;

namespace SciTech.Rpc.Internal
{
#pragma warning disable CA1051 // Do not declare visible instance fields
#nullable disable

    public interface IObjectRequest
    {
        RpcObjectId Id { get; }
    }

    [DataContract]
    public sealed class RpcObjectRequest : IObjectRequest
    {
        [DataMember(Order = 1)]
        public RpcObjectId Id { get; set; }

        public RpcObjectRequest()
        {

        }

        public RpcObjectRequest(RpcObjectId id)
        {
            this.Id = id;
        }
    }

    [DataContract]
    public class RpcServicesQueryResponse
    {
        [DataMember(Order = 1)]
#pragma warning disable CA1819 // Properties should not return arrays
        public string[] ImplementedServices { get; set; }
#pragma warning restore CA1819 // Properties should not return arrays
    }

    [DataContract]
    public sealed class RpcObjectRequest<T1> : IObjectRequest
    {
        [DataMember(Order = 1)]
        public RpcObjectId Id { get; set; }

        [DataMember(Order = 2)]
        public T1 Value1 { get; set; }

        public RpcObjectRequest()
        {

        }

        public RpcObjectRequest(RpcObjectId id, T1 value1)
        {
            this.Id = id;
            this.Value1 = value1;
        }
    }

    [DataContract]
    public sealed class RpcObjectRequest<T1, T2> : IObjectRequest
    {
        [DataMember(Order = 1)]
        public RpcObjectId Id { get; set; }

        [DataMember(Order = 2)]
        public T1 Value1 { get; set; }

        [DataMember(Order = 3)]
        public T2 Value2 { get; set; }

        public RpcObjectRequest()
        {

        }

        public RpcObjectRequest(RpcObjectId id, T1 value1, T2 value2)
        {
            this.Id = id;
            this.Value1 = value1;
            this.Value2 = value2;
        }
    }

    [DataContract]
    public sealed class RpcObjectRequest<T1, T2, T3> : IObjectRequest
    {
        [DataMember(Order = 1)]
        public RpcObjectId Id { get; set; }

        [DataMember(Order = 2)]
        public T1 Value1 { get; set; }

        [DataMember(Order = 3)]
        public T2 Value2 { get; set; }

        [DataMember(Order = 4)]
        public T3 Value3 { get; set; }

        public RpcObjectRequest()
        {

        }

        public RpcObjectRequest(RpcObjectId id, T1 value1, T2 value2, T3 value3)
        {
            this.Id = id;
            this.Value1 = value1;
            this.Value2 = value2;
            this.Value3 = value3;
        }
    }

    [DataContract]
    public sealed class RpcObjectRequest<T1, T2, T3, T4> : IObjectRequest
    {
        [DataMember(Order = 1)]
        public RpcObjectId Id { get; set; }

        [DataMember(Order = 2)]
        public T1 Value1 { get; set; }

        [DataMember(Order = 3)]
        public T2 Value2 { get; set; }

        [DataMember(Order = 4)]
        public T3 Value3 { get; set; }

        [DataMember(Order = 5)]
        public T4 Value4 { get; set; }

        public RpcObjectRequest()
        {

        }

        public RpcObjectRequest(RpcObjectId id, T1 value1, T2 value2, T3 value3, T4 value4)
        {
            this.Id = id;
            this.Value1 = value1;
            this.Value2 = value2;
            this.Value3 = value3;
            this.Value4 = value4;
        }
    }

    [DataContract]
    public sealed class RpcObjectRequest<T1, T2, T3, T4, T5> : IObjectRequest
    {
        [DataMember(Order = 1)]
        public RpcObjectId Id { get; set; }

        [DataMember(Order = 2)]
        public T1 Value1 { get; set; }

        [DataMember(Order = 3)]
        public T2 Value2 { get; set; }

        [DataMember(Order = 4)]
        public T3 Value3 { get; set; }

        [DataMember(Order = 5)]
        public T4 Value4 { get; set; }

        [DataMember(Order = 6)]
        public T5 Value5 { get; set; }

        public RpcObjectRequest()
        {

        }

        public RpcObjectRequest(RpcObjectId id, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5)
        {
            this.Id = id;
            this.Value1 = value1;
            this.Value2 = value2;
            this.Value3 = value3;
            this.Value4 = value4;
            this.Value5 = value5;
        }
    }

    [DataContract]
    public sealed class RpcObjectRequest<T1, T2, T3, T4, T5, T6> : IObjectRequest
    {
        [DataMember(Order = 1)]
        public RpcObjectId Id { get; set; }

        [DataMember(Order = 2)]
        public T1 Value1 { get; set; }

        [DataMember(Order = 3)]
        public T2 Value2 { get; set; }

        [DataMember(Order = 4)]
        public T3 Value3 { get; set; }

        [DataMember(Order = 5)]
        public T4 Value4 { get; set; }

        [DataMember(Order = 6)]
        public T5 Value5 { get; set; }

        [DataMember(Order = 7)]
        public T6 Value6 { get; set; }

        public RpcObjectRequest()
        {

        }

        public RpcObjectRequest(RpcObjectId id, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6)
        {
            this.Id = id;
            this.Value1 = value1;
            this.Value2 = value2;
            this.Value3 = value3;
            this.Value4 = value4;
            this.Value5 = value5;
            this.Value6 = value6;
        }
    }

    [DataContract]
    public sealed class RpcObjectRequest<T1, T2, T3, T4, T5, T6, T7> : IObjectRequest
    {
        [DataMember(Order = 1)]
        public RpcObjectId Id { get; set; }

        [DataMember(Order = 2)]
        public T1 Value1 { get; set; }

        [DataMember(Order = 3)]
        public T2 Value2 { get; set; }

        [DataMember(Order = 4)]
        public T3 Value3 { get; set; }

        [DataMember(Order = 5)]
        public T4 Value4 { get; set; }

        [DataMember(Order = 6)]
        public T5 Value5 { get; set; }

        [DataMember(Order = 7)]
        public T6 Value6 { get; set; }

        [DataMember(Order = 8)]
        public T7 Value7 { get; set; }

        public RpcObjectRequest()
        {

        }

        public RpcObjectRequest(RpcObjectId id, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7)
        {
            this.Id = id;
            this.Value1 = value1;
            this.Value2 = value2;
            this.Value3 = value3;
            this.Value4 = value4;
            this.Value5 = value5;
            this.Value6 = value6;
            this.Value7 = value7;
        }
    }

    [DataContract]
    public sealed class RpcObjectRequest<T1, T2, T3, T4, T5, T6, T7, T8> : IObjectRequest
    {
        [DataMember(Order = 1)]
        public RpcObjectId Id { get; set; }

        [DataMember(Order = 2)]
        public T1 Value1 { get; set; }

        [DataMember(Order = 3)]
        public T2 Value2 { get; set; }

        [DataMember(Order = 4)]
        public T3 Value3 { get; set; }

        [DataMember(Order = 5)]
        public T4 Value4 { get; set; }

        [DataMember(Order = 6)]
        public T5 Value5 { get; set; }

        [DataMember(Order = 7)]
        public T6 Value6 { get; set; }

        [DataMember(Order = 8)]
        public T7 Value7 { get; set; }

        [DataMember(Order = 9)]
        public T8 Value8 { get; set; }

        public RpcObjectRequest()
        {

        }

        public RpcObjectRequest(RpcObjectId id, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8)
        {
            this.Id = id;
            this.Value1 = value1;
            this.Value2 = value2;
            this.Value3 = value3;
            this.Value4 = value4;
            this.Value5 = value5;
            this.Value6 = value6;
            this.Value7 = value7;
            this.Value8 = value8;
        }
    }

    [DataContract]
    public sealed class RpcObjectRequest<T1, T2, T3, T4, T5, T6, T7, T8, T9> : IObjectRequest
    {
        [DataMember(Order = 1)]
        public RpcObjectId Id { get; set; }

        [DataMember(Order = 2)]
        public T1 Value1 { get; set; }

        [DataMember(Order = 3)]
        public T2 Value2 { get; set; }

        [DataMember(Order = 4)]
        public T3 Value3 { get; set; }

        [DataMember(Order = 5)]
        public T4 Value4 { get; set; }

        [DataMember(Order = 6)]
        public T5 Value5 { get; set; }

        [DataMember(Order = 7)]
        public T6 Value6 { get; set; }

        [DataMember(Order = 8)]
        public T7 Value7 { get; set; }

        [DataMember(Order = 9)]
        public T8 Value8 { get; set; }

        [DataMember(Order = 10)]
        public T9 Value9 { get; set; }

        public RpcObjectRequest()
        {

        }

        public RpcObjectRequest(RpcObjectId id, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9)
        {
            this.Id = id;
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
    }

#nullable restore
#pragma warning restore CA1051 // Do not declare visible instance fields
}
