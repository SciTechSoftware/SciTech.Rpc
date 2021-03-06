﻿using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace SciTech.Rpc
{
    [DataContract]
    //[ProtoContract(SkipConstructor = true)]
    public class RpcSingletonRef<TService> where TService : class
    {
        public RpcSingletonRef() { }

        internal RpcSingletonRef(RpcConnectionInfo? connectionInfo) 
        {
            this.ServerConnection = connectionInfo;
        }

        [DataMember(Order = 1)]
        public RpcConnectionInfo? ServerConnection { get; private set; }

        public RpcSingletonRef<TOtherService> Cast<TOtherService>() where TOtherService : class
        {
            return new RpcSingletonRef<TOtherService>(this.ServerConnection);
        }
    }
}
