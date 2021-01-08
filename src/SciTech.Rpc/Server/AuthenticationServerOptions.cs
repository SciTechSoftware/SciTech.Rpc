namespace SciTech.Rpc.Server
{
    public class AuthenticationServerOptions
    {
        public string Name { get; }


        protected AuthenticationServerOptions(string name)
        {
            this.Name = name;
        }
    }

    public class AnonymousAuthenticationServerOptions : AuthenticationServerOptions
    {
        public static AnonymousAuthenticationServerOptions Instance { get; } = new AnonymousAuthenticationServerOptions();

        public AnonymousAuthenticationServerOptions() : base("anonymous")
        {
        }
    }
}
