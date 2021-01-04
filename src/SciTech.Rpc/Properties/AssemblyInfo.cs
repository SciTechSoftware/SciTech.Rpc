using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("SciTech.Rpc.Tests")]

// Make internals visible for logging purposes
[assembly: InternalsVisibleTo("SciTech.Rpc.Grpc")]
[assembly: InternalsVisibleTo("SciTech.Rpc.NetGrpc")]
[assembly: InternalsVisibleTo("SciTech.Rpc.NetGrpc.Client")]
[assembly: InternalsVisibleTo("SciTech.Rpc.Lightweight")]
[assembly: InternalsVisibleTo("SciTech.Rpc.DataContract")]
