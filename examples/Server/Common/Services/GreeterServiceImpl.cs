using System;
using System.Linq;

namespace Greeter
{
    public class GreeterServiceImpl : IGreeterService
    {
        public GreeterServiceImpl()
        {

        }
        public string SayHello(string name)
        {
            return "Hello " + name;
        }
    }
}
