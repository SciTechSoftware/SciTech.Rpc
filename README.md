# SciTech.Rpc

SciTech.Rpc is a high-performance, code-first, remote procedure call framework, designed to handle remote procedure calls on all levels. From  internet deployed services over HTTP/2 (based on [gRPC](https://grpc.io/)), to in-process proxy communication. Similar to WCF, you define service interfaces with the help of attributes in code, and not using external definition files.

**NOTE!** _This is an early preview of SciTech.Rpc. The documentation is very sparse, automated tests are not finished, and there will still be incompatible changes on source level, binary level, and the communication level. The ASP.NET Core implementation of gRPC communication is using a fork of the [grpc-dotnet preview](https://github.com/grpc/grpc-dotnet). The fork contains minor changes necessary to connect SciTech.RPC with the gRPC library. These changes will hopefully be pulled into the main grpc-net project._

Currently there are three comnunication layer implementations for the RPC communication:
* **gRPC**, based on the [native/.NET implementation of gRPC](https://github.com/grpc/grpc)
* **.NET gRPC**, based on the fully managed [ASP.NET Core implementation of gRPC](https://github.com/grpc/grpc-dotnet)

  The gRPC layers can be used to publish services that can be accessed over HTTP/2, by SciTech.RPC clients or by other gRPC clients (e.g. Java, Go, or C++ clients). The RpcCodeGen tool can be used to generate '.proto' files which can be consumed by other gRPC implementations.
* **Pipelines**, a very efficient RPC implementation based on [System.IO.Pipelines](https://devblogs.microsoft.com/dotnet/system-io-pipelines-high-performance-io-in-net/).

  The pipelines implementation provides RPC communication over a range of different protocols: TCP (with SSL), named pipes, and even direct in-process communication.

## Defining RPC interfaces

An interface is marked as an RPC interface by applying the `[RpcService]` attribute, e.g.:

```csharp
[RpcService]
public interface ISimpleService
{
    Task<int> AddAsync(int a, int b);
 
    Task<double> GetValueAsync();
 
    Task SetValueAsync(double value);
}
 
[RpcService]
public interface IBlockingService
{
    int Add(int a, int b);
 
    double Value { get; set; }
}
```

Unlike WCF, there’s no need to mark each member with an attribute and the service must be defined using an interface. All members of an RPCService will be available for remote calls.

Each member of an RPC interface defines an RPC operation. The operation is only identified by name, so it’s not possible to have overloaded members, e.g. it’s not allowed to have one “int Add(int,int)” member and one “double Add(double,double)” member. A simple method operation can be defined by three different interface methods, a blocking member, an asynchronous member, or an asynchronous member with cancellation support (not yet implemented). Consider a method operation like `GetDeviceWithIdAsync(string deviceId)`, which returns a device reference for a specific identifier. This operation can be defined as:

```csharp
[RpcService]
public interface IMailboxManagerService
{
    RpcObjectRef<IMailboxService> GetMailbox(string name);
}
 
[RpcService(ServerDefinitionType = typeof(IMailboxManagerService))]
public interface IMailboxManagerServiceClient : IRpcService
{
    Task<RpcObjectRef<IMailboxServiceClient>> GetMailboxAsync(string name);
}
```

By default the name of a service is the same as the interface defining it, with the initial ‘I’ removed. In this case the client interface will not match the correct service name, so the server side interface has to be specified or the name explicitly assigned (e.g. Name = "MailboxManagerService"). The IRpcService interface seen in the example, provides some useful client side utility methods and properties and is automatically implemented by the proxy. 
 
It is allowed to define multiple variants of the same operation on the client side, e.g. both a blocking and an asynchronous method for the same operation (`GetMailbox` and` GetMailboxAsync`). This is not allowed on the server side, since then the actual method to call would be ambiguous.

## Publishing RPC Services

Remote services can be published as singleton services, similar to WCF and gRPC. A singleton service can be directly accessible using an URL like: grpc://services.company.com/SomeService. SciTech.RPC also offers a more object oriented approach, where specific service instances can be published, and a reference to the published instance can be returned to a connected client.

A service is published on a server with the help of an `RpcServicePublisher`, an `IRpcServiceDefinitionsBuilder` and one or more RPC server implementations (implementing `IRpcServer`). 

The services interfaces of all services that should be hosted by the server must be registered before starting the server. The  interfaces are registered by using the `Register` methods on `IRpcServiceDefinitionsBuilder`.

The RegisterService method will generate a service stub function for each member in the service interface (with the help of compiled LINQ expressions). This stub will be called by the communication layer and it converts the incoming data to a call to the service implementation.
 
After (or before) the host is started, service instances can be published by calling PublishInstance or PublishSingleton. As indicated by the name, a singleton service only allows a single instance to be published and it can be accessed by the client by just the name of the service. A published service instance is identified by an` RpcObjectRef<>`, which includes an identifier of the instance and information about the server that hosts the instance. This is similar to the `ObjRef` class in .NET remoting.

```csharp
var serviceImpl = new SimpleServiceImpl();
var publishScope = host.PublishInstance<ISimpleService>(serviceImpl);
RpcObjectRef<ISimpleService> objectRef = publishScope.Value;
 
// The objectRef can be returned to a connected client, which can 
// use it to retrieve a client side stub of ISimpleService.
 
// ...
 
// Dispose the scope when service instance should no longer
// be published.
publishScope.Dispose();
```

### Return value conversion and auto-publishing

In addition to returning `RpcObjectRefs` from methods, it is also possible to directy return 
RPC service instances. The service instances will be converted by the stub to an `RpcObjectRef` when serialized
and then the client-side proxy will automatically look up the proxy for the `RpcObjectRef` using the 
current connection. Auto-publishing is disabled by default and must be enabled by setting the `IRpcServer.AllowAutoPublishing` property to `true`.

Using return value conversion, the `IMailboxManagerService` defined above, can be changed to:

```csharp
[RpcService]
public interface IMailboxManagerService
{
    IMailboxService GetMailbox(string name);
}
 
[RpcService(ServerDefinitionType = typeof(IMailboxManagerService))]
public interface IMailboxManagerServiceClient : IRpcService
{
    Task<IMailboxServiceClient> GetMailboxAsync(string name);
}
```

Currently return value conversion is hard-coded to handle `RpcObjectRef`, `TService` (i.e. a service interface), `RpcObjectRef[]`, and `TService[]`. In the future `IEnumerable` interfaces and classes of `RpcObjectRef` and `TService` will also be supported. We also plan to support user-defined return value conversions.

For an example of an auto-published service instance, see the [MailboxManager sample](examples/Server/Common/Services/MailBoxManager.cs).

**NOTE!** _Auto-publishing will not work well on a load-balanced service, since all calls on the returned service must be made to the same process. Better support for load-balanced services is being investigated._

### Publish using ASP.NET Core gRPC

To publish a SciTech.Rpc service in ASP.NET Core the NetGrpc server has to be added to the ASP.NET Core startup. The following extensions methods can be used in the startup class:

* `IServiceCollection.AddNetGrpc()`

  Adds the required NetGrpc services to the service collection, and also adds the required ASP.NET Core gRPC services by calling `IServiceCollection.AddGrpc()`.
  
* `IServiceCollection.RegisterRpcService<TService>`

  Registers an RPC service interface to allow RPC singletons and instances to be published at a later stage.
  
* `IApplicationBuilder.PublishSingleton`

  Publishes singleton service to the registered IRpcServicePublisher. Singeltons can be published using a specific instance, a factory method, or by relying on the `IServiceProvider` provided when processing a gRPC call.
  
* `IEndpointRouteBuilder.MapNetGrpcServices`
  
  To make registered and published services available as gRPC HTTP/2 operations, the service operations have to be mapped as HTTP/2 endpoints using the `MapNetGrpcServices` extension method. `MapNetGrpcServices`can be called from the configuration action provided to `IApplicationBuilder.UseEndpoints`method.

For an example on how to publish using ASP.NET Core gRPC, see the [`Startup`](examples/Server/NetGrpcServer/Startup.cs) class of the [NetGrpcServer](examples/Server/NetGrpcServer) example.

### Publish using gRPC
The `GrpcServer` class is used to publish services using the native/.NET implementation of gRPC. 

```csharp
var definitionsBuilder = new RpcServiceDefinitionBuilder();
definitionsBuilder
    .RegisterService<IBlockingService>()
    .RegisterService<ISimpleService>();
IRpcServerHost server = new GrpcServer( definitionsBuilder );
server.AddEndPoint(new GrpcEndPoint("localhost", GrpcTestPort, false));
 
server.Start();
```

For more information, see the [GrpcAndPipelinesServer](examples/Server/GrpcAndPipelinesServer) example.

### Publish using Pipelines
The `RpcPipelinesServer` class is used to publish services using the Pipelines RPC implementation. 

```csharp
var definitionsBuilder = new RpcServiceDefinitionBuilder();
definitionsBuilder
    .RegisterService<IBlockingService>()
    .RegisterService<ISimpleService>();
IRpcServerHost server = new RpcPipelinesServer( definitionsBuilder, null, new ProtobufSerializer() );
server.AddEndPoint(new TcpPipelinesEndPoint("127.0.0.1", 50052, false));
 
server.Start();
```

For more information, see the [GrpcAndPipelinesServer](examples/Server/GrpcAndPipelinesServer) example.

## Connecting to RPC Services

To access remote services from the client side, proxy implementations of the service interface are used. 
 
The `IRpcServerConnection` and `IRpcServerConnectionManager` interfaces can be used retrieve a client proxy implementation of the service interface. The `IRpcServerConnection` interface represents a specific connection to one server and can be used to retrieve proxies for that specific server. The `IRpcServerConnectionManager` interface allows proxies to be retrieved for any reachable server, based on the server information in `RpcObjectRef<>`. 

The code below shows how the `GrpcServerConnection` implementation of `IRpcServerConnection` can be used to connect to a gRPC service (using the default `GrpcProxyGenerator` and a `ProtobufSerializer`). 

```csharp
IRpcServerConnection connection = new GrpcServerConnection(new RpcServerConnectionInfo("grpc://127.0.0.1:50051" ));
 
var managerService = connection.GetServiceSingleton<IMailboxManagerService>(); 
RpcObjectRef<IMailboxService> mailboxRef = managerServer.GetMailbox("MailboxName");
var mailboxService = connection.GetServiceInstance(mailboxRef);
// ...
```

The [GreeterClient example](examples/Clients/GreeterClient/Program.cs) shows how an `RpcServerConnectionManager` can be initialized to provide connections to a gRPC server and a Pipelines server.

## EventHandlers and Delegates

It is also allowed to include events in the service definition. The events must currently be declared using EventHandler, or EventHandler<TEventArgs>, but support for any delegate (returning void) type will be implemented. The event handler implementation is completely transparent, you can use the events as you would with a local interface. However, the RPC proxy is associated with a SynchronizationContext, which allows event callbacks to be properly marshalled to the correct synchronization context, e.g. the main user interface thread. 
  
The SynchronizationContext can be specified when retrieving the service proxy using GetRpcServiceInstance,for example:

```csharp
var clientService = connection.GetServiceInstance(objectRef, this.synchronizationContext);
```

## Exception and Error Handling

By default, any exception thrown by the service operation will be propagated to the communication layer (e.g. gRPC or Pipelines). The communication layer will normally generate some kind of error return which will cause another exception on the client side (e.g. a `Grpc.Core.RpcException`). The client side proxy will investigate exceptions thrown by the communication layer and convert them to a suitable SciTech.Rpc exception. This will normally be an `RpcFailureException` in case of an exception in the operation server side implementation. The RpcFailureException will just include the message provided with underlying exception, which is usually not very informative (since the communication layer by default doesn't include exception details).

To allow an operation to provide more detailed error information, it is possible to tag the operation with an `[RpcFault]` attribute. This is similar to the `[OperationFault]` attribute in WCF. Unlike WCF, it is also possible to apply the fault attribute at the service definition level, in which case the attribute will apply to all operations in the service.

The `RpcFault` attribute can be applied with just a fault code, or it can accept a serializable fault details type that can be used to provide additional details about the fault. If an operation is tagged with the RpcFault attribute, the service implementation can throw an `RpcFaultException`to propagate error information back to the client.

_**NOTE!** The exception handling design is not fully finished yet. More details and examples about exception handling will be added soon._

The following exceptions may be thrown by the SciTech.Rpc framework:

* `RpcFailureException`: Thrown when an undeclared exception occurs within an  operation handler.
* `RpcCommunicationExcepton`: Thrown when there is a communication error with the RPC server, e.g. server unreachable, server disconnected.
* `RpcDefinitionException`: Thrown when there is an error in an RPC definition interface. May occur when service interfaces are registered.
* `RpcFaultException`, `RpcFaultException<TFault>`: Thrown when a declared fault exception occurs within an operation handler. Also used by the service implementation to provide error information to the client.
* `RpcServiceUnavailableException`: Thrown when a service singleton or instance cannot be reached because it is not available on the server.

### Exception converters

To make RPC exception handling more streamlined and easier to use, it is also possible to register exception converters. An exception converter can be used to convert an ordinary exception to an RPC fault on the server side, and to convert an RPC fault to an ordinary exception on the client side. 

Exceptions converts are implemented using the `IRpcServerExceptionConverter` and `IRpcClientExceptionConverter` interfaces. A default implementation is provided by the `RpcExceptionConverter<TException>` class. 

Exception converters are applied to RPC operations by using the `[RpcExceptionConverter]`attribute (not implemented yet), or by registering it using the `IRpcServiceDefinitionBuilder` on the server side, or using the `IRpcProxyDefinitionsBuilder` on the client side.

_**NOTE!** The exception handling design is not fully finished yet. More details and examples about exception handling will be added soon._

## Authentication

It is possible to provide client credentials to the service at connection level (channel), or at call level. Connection level credentials are provided as an SSL certificate when creating a connection. For the gRPC implementation, this is based on the [`Grpc.Core.SslCredentials`](https://grpc.github.io/grpc/csharp/api/Grpc.Core.SslCredentials.html) class. 

_**NOTE!** SSL support and connection levels credentials have not yet been implemented for the Pipelines communication. It will be implemented soon._

Per call credentials can be provided by attaching security tokens to the call metadata. Attaching and reading call metadata is performed with the help of the `RpcClientCallInterceptor`and the `RpcServerCallInterceptor` delegates. These delegates are registered using `IRpcServer.AddCallInterceptor` and `IRpcServerConnectionManager.AddCallInterceptor`. 

_**NOTE!** The authentication design is not fully finished yet. More details and examples about authentication will be added._

## Serialization

By default gRPC serialization is based on the [Protobuf (Protocol buffer)](https://developers.google.com/protocol-buffers/) serializer. A very efficient binary serialization protocol which is also highly version tolerant. SciTech.Rpc includes a Protobuf serializer based on the [protobuf-net](https://github.com/mgravell/protobuf-net) implementation of Protobuf. It also includes a DataContract serializer (binary by default), and other serializers will be implemented, e.g. Json.NET. 

## Building SciTech.Rpc

To build the SciTech.Rpc solution, you need to use Visual Studio 2019 v16.1 and have .NET Core 3.0 Preview 5 installed. 

**NOTE!** _After upgrading to Visual Studio 2019 v16.1, the Roslyn process started to crash when editing files and we had to remove multi-targeting from the projects. This will hopefully be solved soon, maybe in .NET Core 3.0 Preview 6._

To build the solution for all available targets (currently .NET Framework 4.6.1+, .NET Standard 2.0, and .NET Core 2.0), the property `EnableMultiTargeting` should be set to "true". The ASP.NET Core gRPC implementation is only available for .NET Core 3.0.

To build the solution using the command line, use the following command:

`dotnet build -p:EnableMultiTargeting=true --configuration Release`

To run the unit tests, use the following command:

`dotnet test -p:EnableMultiTargeting=true --configuration Release`

## Feedback and contributions

Feedback and contributions are welcome!

SciTech.Rpc was initially designed as a replacement for .NET Remoting in a large legacy project that is being upgraded to from .NET Framework 2.0 to .NET Core 3.0. It will also be used as the communication foundation of the cloud profiling support in the coming version 6.0 of [.NET Memory Profiler](https://memprofiler.com).

So SciTech.Rpc will be used in two rather large projects and we are committed to creating a stable 1.0 release within a few months, hopefully before the official release of .NET Core 3.0. 

We would very much appreciate any feedback, issues, or pull requests you have. You can use the Github project page for issues and pull requests, or you can [e-mail the product owner](mailto:andreas@scitech.se), Andreas Suurkuusk,  with any feedback or comments you have. 

 
