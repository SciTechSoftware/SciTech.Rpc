using System;

namespace SciTech.Rpc.Lightweight.Lightweight.Server.Internal
{
    public class InvalidOperation : Exception
    {
        public InvalidOperation(string str) : base(str)
        {
        }
    }
}
