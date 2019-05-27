using System;
using System.Collections.Generic;
using System.Text;

namespace SciTech.Rpc
{
    /// <summary>
    /// Attribute used to indicate that an RPC service or operation will not provide any fault information. 
    /// </summary>

    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Property | AttributeTargets.Method)]
    public class RpcNoFaultAttribute : Attribute
    {
    }
}
