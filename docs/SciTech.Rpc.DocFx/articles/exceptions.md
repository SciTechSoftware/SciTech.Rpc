## Exception and Error Handling

By default, error information can be returned from an RPC method using the RpcFaultException. If an `RpcFaultException` is thrown out of a service method, it will be serialized an rethrown on the client side. This exception class contains a fault code that can be used to identify the fault and a standard exception message. Unless the service is configured to provide detailed exception information, the fault code and and the message are the only properties that are propagated to the client. 

To allow an operation to provide more detailed error information, it is possible to tag the operation with an `[RpcFault]` attribute. This is similar to the `[FaultContract]` attribute in WCF. Unlike WCF, it is also possible to apply the fault attribute at the service definition level, in which case the attribute will apply to all operations in the service. Using `[RpcFault]` it is possible to specify a fault details type that will be serialized with the errpor and can include additional fault information.

Any other exception thrown by the service operation will be propagated to the communication layer (e.g. gRPC or Lightweight). The communication layer will normally generate some kind of error return which will cause another exception on the client side (e.g. a `Grpc.Core.RpcException`). The client side proxy will investigate exceptions thrown by the communication layer and convert them to a suitable SciTech.Rpc exception. This will normally be an `RpcFailureException` in case of an exception in the operation server side implementation. The RpcFailureException will just include the message provided with underlying exception, which is usually not very informative (since the communication layer by default doesn't include exception details).

The following exceptions may be thrown by the SciTech.Rpc framework:

* `RpcFailureException`: Thrown when an undeclared exception occurs within an  operation handler.
* `RpcServiceUnavailableException`: Thrown when a service singleton or instance cannot be reached because it is not available on the server.
* `RpcCommunicationException`: Thrown when there is a communication error with the RPC server, e.g. server unreachable, server disconnected.
* `RpcDefinitionException`: Thrown when there is an error in an RPC definition interface. May occur when service interfaces are registered.
* `RpcFaultException`, `RpcFaultException<TFault>`: Thrown when a declared fault exception occurs within an operation handler. Also used by the service implementation to provide error information to the client.

### Exception converters

To make RPC exception handling more streamlined and easier to use, it is also possible to register exception converters. An exception converter can be used to convert an ordinary exception to an RPC fault on the server side, and to convert an RPC fault to an ordinary exception on the client side. 

Exceptions converts are implemented using the `IRpcServerExceptionConverter` and `IRpcClientExceptionConverter` interfaces. A default implementation is provided by the `RpcExceptionConverter<TException>` class. 

Exception converters are applied to RPC operations by using the `[RpcExceptionConverter]` attribute, or by registering it using the `IRpcServiceDefinitionBuilder` on the server side, or using the `IRpcProxyDefinitionsBuilder` on the client side.


