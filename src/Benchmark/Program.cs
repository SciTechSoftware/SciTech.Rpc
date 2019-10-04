using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.CsProj;
using ProtoBuf.Meta;
using SciTech.Rpc;
using SciTech.Rpc.Client;
using SciTech.Rpc.Grpc.Client;
using SciTech.Rpc.Grpc.Server;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Lightweight;
using SciTech.Rpc.Lightweight.Server;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server;
using System;
using System.IO;
using System.Security.Cryptography;

namespace Benchmark
{

    public enum RpcConnectionType
    {
        LightweightInproc,
        LightweightTcp,
        LightweightSslTcp,
        LightweightNamedPipe,
        Grpc,
        NetGrpc
    }

    [RpcService]
    public interface ISimpleService
    {
        int Add(int a, int b);
    }

    public class SimpleServiceImpl : ISimpleService
    {
        public int Add(int a, int b)
        {
            return a + b;
        }
    }

    public class MultipleRuntimes : ManualConfig
    {
        public MultipleRuntimes()
        {
            Add(Job.Default.With(CsProjCoreToolchain.NetCoreApp30).WithIterationCount(3)); // .NET Core 2.1
            Add(Job.Default.With(CsProjCoreToolchain.NetCoreApp21).WithIterationCount(3)); // .NET Core 2.1
            Add(Job.Default.With(CsProjCoreToolchain.NetCoreApp20).WithIterationCount(3)); // .NET Core 2.1
            Add(Job.Default.With(CsProjClassicNetToolchain.Net472).WithIterationCount(3)); // NET 4.6.2
            Add(Job.Default.With(CsProjClassicNetToolchain.Net472).With(Platform.X86).WithIterationCount(3)); // NET 4.6.2
        }
    }

    //[ClrJob, CoreJob, LegacyJitX86Job, ShortRunJob]
    //[ShortRunJob]
    [MemoryDiagnoser]
    [Config(typeof(MultipleRuntimes))]
    public class SimpleServiceCall
    {
        public static readonly RuntimeTypeModel DefaultTypeModel = RuntimeTypeModel.Create().AddRpcTypes();

        IRpcServer server;

        IRpcServerConnection clientConnection;
        ISimpleService clientService;

        public SimpleServiceCall()
        {

        }


        [Params(RpcConnectionType.Grpc, RpcConnectionType.LightweightInproc)]
        public RpcConnectionType ConnectionType;

        [GlobalSetup]
        public void GlobalSetup()
        {
            var definitionsProvider = new RpcServiceDefinitionBuilder();

            switch (this.ConnectionType)
            {
                case RpcConnectionType.Grpc:
                    this.server = new GrpcServer(definitionsProvider);
                    this.server.AddEndPoint(new GrpcServerEndPoint("localhost", 50051, false, Grpc.Core.ServerCredentials.Insecure));
                    this.server.ServicePublisher.PublishSingleton<ISimpleService>(new SimpleServiceImpl());
                    this.server.Start();

                    this.clientConnection = new GrpcServerConnection(new RpcServerConnectionInfo(new Uri("grpc://localhost:50051")));
                    clientService = this.clientConnection.GetServiceSingleton<ISimpleService>();
                    break;
                case RpcConnectionType.LightweightInproc:
                    {
                        var connector = new DirectLightweightRpcConnector(new RpcClientOptions { Serializer = new ProtobufRpcSerializer() });
                        this.server = new LightweightRpcServer(definitionsProvider, null, new RpcServerOptions { Serializer = connector.Connection.Options.Serializer });
                        this.server.AddEndPoint(connector.EndPoint);
                        this.server.ServicePublisher.PublishSingleton<ISimpleService>(new SimpleServiceImpl());
                        this.server.Start();

                        this.clientConnection = connector.Connection;
                        clientService = this.clientConnection.GetServiceSingleton<ISimpleService>();
                        break;
                    }
            }
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            this.clientConnection.ShutdownAsync().Wait();
            this.server.ShutdownAsync().Wait();
            this.server.Dispose();
        }



        //[Benchmark]
        //public int Protobuf()
        //{
        //    RpcObjectRequest<int, int> request = new RpcObjectRequest<int, int>(RpcObjectId.Empty, 5, 6);
        //    RpcObjectRequest<int, int> request2;
        //    using (var ms = new MemoryStream())
        //    {
        //        DefaultTypeModel.Serialize(ms, request);
        //        ms.Seek(0, SeekOrigin.Begin);
        //        request2 = (RpcObjectRequest<int, int>)DefaultTypeModel.Deserialize(ms, null, typeof(RpcObjectRequest<int, int>));
        //    }

        //    RpcResponse<int> response = new RpcResponse<int>(11);
        //    RpcResponse<int> response2;
        //    using (var ms = new MemoryStream())
        //    {
        //        DefaultTypeModel.Serialize(ms, response);
        //        ms.Seek(0, SeekOrigin.Begin);
        //        response2 = (RpcResponse<int>)DefaultTypeModel.Deserialize(ms, null, typeof(RpcResponse<int>));
        //    }

        //    return request2.Value1 + request2.Value2 + response2.Result;
        //}

        [Benchmark]
        public int SimpleCall() => this.clientService.Add(5, 6);
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            //var b = new SimpleServiceCall();
            //b.ConnectionType = RpcConnectionType.LightweightInproc;
            //b.GlobalSetup();
            //int res = b.SimpleCall();
            //b.GlobalCleanup();

            //Console.WriteLine(res);

            //var summary = BenchmarkRunner.Run<SimpleServiceCall>();
            var summary = BenchmarkRunner.Run<SimpleServiceCall>();

            //BenchmarkRunner.Run<SimpleServiceCall>(
            //    DefaultConfig.Instance
            //    .With(Job.Default.With(CsProjCoreToolchain.NetCoreApp30)));
        }
    }
}
