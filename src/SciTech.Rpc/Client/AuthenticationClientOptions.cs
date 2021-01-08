namespace SciTech.Rpc.Client
{
    public class AuthenticationClientOptions 
    {
        protected AuthenticationClientOptions(string name)
        {
            this.Name = name;
        }

        public string Name { get; }

    }

}
