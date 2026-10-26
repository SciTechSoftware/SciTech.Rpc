#region Copyright notice and license
// Copyright (c) SciTech Software AB and contributors.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License.
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace SciTech.Rpc.Internal
{
    /// <summary>
    /// A message of the request stream of a client-streaming call, for transports where a call only has a single request
    /// stream, such as gRPC. The first message of the stream contains the <see cref="Request"/>, i.e. the object id and the
    /// other parameters of the call, and each following message contains one <see cref="Item"/> of the stream parameter.
    /// </summary>
    /// <remarks>
    /// Since the position of a message determines what it contains, no discriminator is needed, also for value type items.
    /// In Protobuf, the message corresponds to a <c>oneof</c> of the request and the item.
    /// </remarks>
    [DataContract]
    public sealed class RpcClientStreamingMessage<TRequest, TStreamItem>
        where TRequest : class
    {
        public RpcClientStreamingMessage()
        {
        }

        [DataMember(Order = 1)]
        public TRequest? Request { get; set; }

        [DataMember(Order = 2)]
        [AllowNull]
        public TStreamItem Item { get; set; }
    }
}
