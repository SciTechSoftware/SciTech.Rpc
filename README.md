# SciTech.Rpc

SciTech.Rpc is a high-performance, code-first remote procedure call framework for .NET. Like WCF, services are
defined by attributed interfaces in code, not by external definition files. It handles remote calls on all levels,
from services on the internet over HTTP/2 (using [gRPC](https://grpc.io/)) to calls between processes on the same
machine and within a process.

SciTech.Rpc has been used in production for several years. Version 1.0 is currently in the release candidate
stage.

There are two communication layers:

* **Lightweight**, a very efficient implementation based on
  [System.IO.Pipelines](https://learn.microsoft.com/dotnet/standard/io/pipelines). It communicates over TCP (with
  TLS or Negotiate authentication), named pipes, or directly within a process, and includes service discovery on the
  local network. The wire format is specified in [docs/lightweight-protocol.md](https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/docs/lightweight-protocol.md).
* **NetGrpc**, based on the fully managed [gRPC for .NET](https://github.com/grpc/grpc-dotnet). Services are
  hosted in ASP.NET Core and can be accessed over HTTP/2.

The libraries target .NET Framework 4.7.2, .NET 8 and .NET 10. The NetGrpc server requires ASP.NET Core (.NET 8 or
later).

## Packages

| Package | Contents |
|---|---|
| [SciTech.Rpc](https://www.nuget.org/packages/SciTech.Rpc/) | The core framework, used by the other packages. Includes a Roslyn analyzer for RPC service interfaces. |
| [SciTech.Rpc.Lightweight](https://www.nuget.org/packages/SciTech.Rpc.Lightweight/) | The Lightweight client and server. |
| [SciTech.Rpc.NetGrpc](https://www.nuget.org/packages/SciTech.Rpc.NetGrpc/) | The gRPC server, for ASP.NET Core. |
| [SciTech.Rpc.NetGrpc.Client](https://www.nuget.org/packages/SciTech.Rpc.NetGrpc.Client/) | The gRPC client. |
| [SciTech.Rpc.Protobuf](https://www.nuget.org/packages/SciTech.Rpc.Protobuf/) | A Protobuf serializer, based on [protobuf-net](https://github.com/protobuf-net/protobuf-net). |
| [SciTech.Rpc.DataContract](https://www.nuget.org/packages/SciTech.Rpc.DataContract/) | A DataContract serializer. |
| [SciTech.Rpc.Resources](https://www.nuget.org/packages/SciTech.Rpc.Resources/) | Remote resources and locks. |
| [SciTech.Core](https://www.nuget.org/packages/SciTech.Core/) | Utility types used by SciTech.Rpc. |

## Defining RPC services

An interface is marked as an RPC service by applying the `[RpcService]` attribute:

```csharp
[RpcService]
public interface IThermostatService
{
    Task<double> GetTemperatureAsync();

    Task SetTargetTemperatureAsync(double temperature, CancellationToken cancellationToken);

    double TargetTemperature { get; set; }

    event EventHandler<TemperatureChangedEventArgs> TemperatureChanged;
}
```

All public members of an RPC service are RPC operations, so there's no need to mark each member with an
attribute. By default, the name of a service is the name of the interface without the initial "I", in the
namespace of the interface. It can be changed with the `Name` and `Namespace` properties of `[RpcService]`.

An operation is identified by its name only, so overloaded members are not allowed. An operation can be
implemented as a blocking method, an asynchronous method, or an asynchronous method with a `CancellationToken`.
The client and server definitions may differ: a client side interface can, for example, define both a blocking
and an asynchronous variant of the same operation, as long as it refers to the server side definition:

```csharp
[RpcService(ServerDefinitionType = typeof(IThermostatService))]
public interface IThermostatServiceClient : IThermostatService, IRpcProxy
{
    Task<double> GetTargetTemperatureAsync();
}
```

The supported kinds of operations are:

| Operation | Declaration |
|---|---|
| Unary call | `int Add(int a, int b);` or `Task<int> AddAsync(int a, int b);` |
| Property | `double Value { get; set; }` |
| Event | `event EventHandler<TEventArgs> Changed;` |
| Server streaming | `IAsyncEnumerable<Reading> GetReadings(int count, CancellationToken cancellationToken);` |
| Client streaming | `Task<int> Upload(string name, IAsyncEnumerable<Sample> samples);` |
| Callback | `Task MeasureAsync(int count, Action<Reading> callback);` |

Event handlers are invoked in the synchronization context provided when the proxy was retrieved, e.g. the user
interface thread.

## Publishing services

A service implementation is published by an `IRpcServer`, either as a singleton, which clients access by the
service name, or as an instance. A published instance is identified by an `RpcObjectRef<TService>`, which can be
returned to clients, similar to an `ObjRef` in .NET Remoting:

```csharp
var server = new LightweightRpcServer();
server.AddEndPoint(new TcpRpcEndPoint("localhost", 50052, false));

// Singletons are accessed by service name.
server.PublishSingleton<IThermostatService>(new ThermostatService());

server.Start();

// Instances are identified by an RpcObjectRef, which can be returned to a client.
using var publishedSensor = server.ServicePublisher.PublishInstance<ISensorService>(new SensorService());
RpcObjectRef<ISensorService> sensorRef = publishedSensor.Value;

// ...

await server.ShutdownAsync();
```

The service interfaces are registered with an `RpcServiceDefinitionsBuilder`. Publishing a service registers its
interfaces automatically, but registration is only possible before the server is started. A service returned by an
operation, e.g. `ISensorService GetSensor(string name)`, is converted to an `RpcObjectRef` and published
automatically if the `AllowAutoPublish` option is enabled.

See the [LightweightServer](https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/examples/Server/LightweightServer/Program.cs) example for a complete server.

### Publishing with ASP.NET Core gRPC

The NetGrpc server is configured in the ASP.NET Core startup:

* `IServiceCollection.AddNetGrpc()` adds the NetGrpc services, and the ASP.NET Core gRPC services.
* `IServiceCollection.RegisterRpcService<TService>()` registers a service interface.
* `IApplicationBuilder.PublishRpcSingleton<TService, TServiceImpl>()` publishes a singleton, e.g. one that is
  created by the service provider for each call.
* `IEndpointRouteBuilder.MapNetGrpcServices()` maps the registered services to gRPC endpoints. Services must be
  registered before this is called.

See the [NetGrpcServer](https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/examples/Server/NetGrpcServer/Startup.cs) example.

## Connecting to services

A client uses a proxy, which implements the service interface, retrieved from a connection:

```csharp
await using var connection = new TcpRpcConnection(new RpcConnectionInfo(new Uri("lightweight.tcp://localhost:50052")));

var thermostat = connection.GetServiceSingleton<IThermostatServiceClient>();
double temperature = await thermostat.GetTemperatureAsync();

var sensor = connection.GetServiceInstance(sensorRef);
```

`NetGrpcConnection` connects to a NetGrpc server, e.g. at `grpc://localhost:50051`, and `NamedPipeRpcConnection`
and `InprocRpcConnection` provide the other Lightweight transports. An `RpcConnectionManager` creates and reuses
connections to any reachable server, based on the connection info of an `RpcObjectRef`. See the
[GreeterClient](https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/examples/Clients/GreeterClient/Program.cs) example.

`IRpcProxy.TryCastAsync<TService>()` checks whether the object behind a proxy implements another service, for
example a newer version of a service.

## Service discovery

A Lightweight server with a `LightweightDiscoveryEndPoint` can be discovered by clients on the local network,
over UDP multicast. A client can ask for the singleton services it needs, and then only servers that publish at
least one of them answer:

```csharp
var discoveryClient = new LightweightDiscoveryClient();
discoveryClient.ServerDiscovered += (s, e) =>
{
    var server = e.DiscoveredServer;

    // Services lists which of the requested services the server publishes.
    bool isCompatible = server.Services.Contains("Thermostat.ThermostatService2");
    server.Properties.TryGetValue("version", out var version);
    Console.WriteLine($"{server.ConnectionInfo.DisplayName}, version {version}, compatible: {isCompatible}");
};

await discoveryClient.FindServicesAsync(
    new[] { "Thermostat.ThermostatService", "Thermostat.ThermostatService2" },
    cancellationToken);
```

A server describes itself with server properties, application-defined key/value pairs set in
`RpcServerOptions.ServerProperties`, e.g. a product name and version. Discovery is unauthenticated, so it should
only be used as a pre-filter. After connecting, `IRpcChannel.GetServerInfoAsync()` returns the same information
over the authenticated connection, together with the singleton services the server publishes.

## Versioning services

A published service interface should not be changed: operations must not be removed, and their parameters and
return types must not change. A client can't detect such a change, and a changed signature may be deserialized
incorrectly. New operations are added in a new interface, e.g. `IThermostatService2 : IThermostatService` with its
own service name, and clients check whether it's available: by discovery or `GetServerInfoAsync` for a
singleton, and by `TryCastAsync` for an object.

## Errors and faults

An exception thrown by a service implementation reaches the client as an `RpcFailureException`, with only the
exception message. To provide more detailed error information, an operation, or a whole service, can declare
faults with the `[RpcFault]` attribute, similar to `[FaultContract]` in WCF. The implementation then throws an
`RpcFaultException<TFault>`, which reaches the client with its fault details.

Exception converters, implemented by deriving from `RpcExceptionConverter<TException, TFault>`, convert ordinary
exceptions to faults on the server side, and back to exceptions on the client side. They're registered in
`RpcServerOptions.ExceptionConverters` and `RpcClientOptions.ExceptionConverters`, or with the
`[RpcFaultConverter]` attribute. Faults are handled the same way for unary and streaming operations.

The framework throws these exceptions:

* `RpcFailureException`: an undeclared exception in an operation, or another failure of the call.
* `RpcFaultException`, `RpcFaultException<TFault>`: a declared fault.
* `RpcServiceUnavailableException`: the service singleton or instance is not published on the server.
* `RpcCommunicationException`: a communication error, e.g. the server is unreachable or disconnected.
* `RpcDefinitionException`: an error in an RPC service definition.
* `TimeoutException` and `OperationCanceledException`: the call timed out or was cancelled.

## Authentication

A Lightweight TCP end point can require TLS (`SslServerOptions`) or Negotiate (`NegotiateServerOptions`)
authentication, and the client provides the matching `SslClientOptions` or `NegotiateClientOptions`. NetGrpc uses
the authentication of ASP.NET Core, and operations or services can require authorization with `[RpcAuthorize]`
and `[RpcAllowAnonymous]`.

Metadata, e.g. security tokens, can be attached to calls with `RpcClientCallInterceptor` and read with
`RpcServerCallInterceptor`, registered in the client and server options.

## Serialization

Lightweight uses JSON (`JsonRpcSerializer`) by default, and NetGrpc uses Protobuf (`ProtobufRpcSerializer`).
The serializer is configured in the client and server options, and both sides must use the same one. Types that
are serialized polymorphically are declared with `[RpcKnownType]`, which is supported by all serializers.

## Building SciTech.Rpc

Building requires the .NET SDK specified in [global.json](https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/global.json). Running the .NET Framework tests requires
Windows.

```
dotnet build --configuration Release
dotnet test --configuration Release --framework net8.0
```

The tests use fixed TCP ports, so run one target framework at a time.

## Feedback and contributions

Feedback, issues and pull requests are welcome. Use the GitHub issues and pull requests, or
[e-mail the maintainer](mailto:andreas@scitech.se), Andreas Suurkuusk. Security issues should be reported as
described in [SECURITY.md](https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/SECURITY.md). Contributors are listed in [CONTRIBUTORS.md](https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/CONTRIBUTORS.md).

SciTech.Rpc is licensed under the [BSD 3-Clause License](https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE).
