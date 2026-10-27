# Exception handling

An exception thrown by a service implementation is not sent to the client as it is. The exception may contain
information that the client should not see, and the client may not even know the exception type. Instead, the
client gets an `RpcFailureException` with a generic message, unless the exception is turned into an *RPC fault*.

An RPC fault consists of:

* A **fault code**, a string that identifies the fault, e.g. `"DeviceAction"`.
* A **message**, which is sent to the client.
* Optional **fault details**, an object with more information about the fault, serialized with the serializer of the
  operation.

There are two ways to create faults:

* **Declared faults:** the operation declares the fault with `[RpcFault]`, and the implementation throws an
  `RpcFaultException<TFault>`. The client catches the same exception type.
* **Exception converters:** the implementation throws an ordinary exception, which an exception converter turns into
  a fault on the server side. On the client side, the converter turns the fault back into the exception. Neither the
  implementation nor the client code needs to know about RPC faults.

Exception converters are the most convenient choice in most applications, since the service implementation and the
client code use ordinary exceptions.

## Declared faults

A fault is declared with `[RpcFault]`, and the type of the fault details:

```csharp
[DataContract]
public class TemperatureFault
{
    [DataMember(Order = 1)]
    public double Temperature { get; set; }
}

[RpcService]
public interface IThermostatService
{
    [RpcFault(typeof(TemperatureFault))]
    Task SetTargetTemperatureAsync(double temperature);
}
```

The implementation throws an `RpcFaultException<TFault>`:

```csharp
public Task SetTargetTemperatureAsync(double temperature)
{
    if (temperature > MaxTemperature)
    {
        throw new RpcFaultException<TemperatureFault>("The temperature is too high.", new TemperatureFault { Temperature = temperature });
    }

    // ...
}
```

and the client catches it:

```csharp
try
{
    await thermostat.SetTargetTemperatureAsync(120);
}
catch (RpcFaultException<TemperatureFault> e)
{
    Console.WriteLine($"{e.Message} ({e.Fault.Temperature})");
}
```

The fault code is the name of the details type, unless it's specified with `[RpcFaultDetails("FaultCode")]` on the
details type. The details type must be serializable by the serializer of the operation, e.g. with `[DataContract]`
and `[DataMember(Order = n)]` for Protobuf.

`[RpcFault]` can be applied to an operation, or to the service interface, which declares the fault for all of its
operations. Fault details are only sent to the client if the fault is declared: otherwise the client couldn't
deserialize them. An undeclared `RpcFaultException<TFault>` reaches the client as a plain `RpcFaultException` with
the fault code and the message. A plain `RpcFaultException`, without details, can always be thrown:

```csharp
throw new RpcFaultException("OutOfRange", "The temperature is out of range.");
```

## Exception converters

An exception converter implements `IRpcServerExceptionConverter`, which creates a fault from an exception on the
server side, and `IRpcClientExceptionConverter`, which creates the exception from the fault on the client side.
`RpcExceptionConverter<TException>` is a ready-made converter for exceptions that only need a message: it sends the
exception message with the fault, and creates the exception with its `Exception(string message)` constructor.

Converters can be applied with an attribute, for a service or an operation, or registered in the options, for all
services.

### Applying converters with attributes

`[RpcFaultConverter]` with a fault code and an exception type applies an `RpcExceptionConverter<TException>`:

```csharp
[RpcService]
[RpcFaultConverter(ThermostatFaults.DeviceAction, typeof(DeviceActionException))]
[RpcFaultConverter(ThermostatFaults.UnauthorizedAccess, typeof(UnauthorizedAccessException))]
public interface IThermostatService
{
    Task SetTargetTemperatureAsync(double temperature);

    [RpcFaultConverter(ThermostatFaults.CalibrationFailed, typeof(CalibrationException))]
    Task CalibrateAsync();
}
```

The fault codes are best defined as constants in a shared class, since the same codes are used by many services:

```csharp
public static class ThermostatFaults
{
    public const string CalibrationFailed = "CalibrationFailed";
    public const string DeviceAction = "DeviceAction";
    public const string UnauthorizedAccess = "UnauthorizedAccess";
}
```

The implementation just throws the exception, and the client catches it, with the original message:

```csharp
try
{
    await thermostat.CalibrateAsync();
}
catch (CalibrationException e)
{
    Console.WriteLine(e.Message);
}
```

By default, sub types of the exception type are also converted. Set `IncludeSubTypes = false` to only convert the
exact type. The client always creates an instance of the exception type itself.

A converter type can also be applied, e.g. a converter with fault details (see below):
`[RpcFaultConverter(typeof(TemperatureExceptionConverter))]`. The converter type must have a default constructor.

### Registering converters for all services

Converters that are used by all services, e.g. for authentication and authorization exceptions, are registered in
the options, on both the server and the client side:

```csharp
public static class ThermostatFaults
{
    // ...

    public static readonly IReadOnlyList<IRpcExceptionConverter> ExceptionConverters = new IRpcExceptionConverter[]
    {
        new RpcExceptionConverter<AuthenticationException>("Authentication"),
        new RpcExceptionConverter<UnauthorizedAccessException>(UnauthorizedAccess),
    };
}

// Server
var serverOptions = new RpcServerOptions();
serverOptions.ExceptionConverters.AddRange(ThermostatFaults.ExceptionConverters);
var server = new LightweightRpcServer(options: serverOptions);

// Client
var clientOptions = new RpcClientOptions();
clientOptions.ExceptionConverters.AddRange(ThermostatFaults.ExceptionConverters);
var connection = new TcpRpcConnection(connectionInfo, options: clientOptions);
```

With dependency injection, the NetGrpc server options are configured with
`services.AddNetGrpc(o => o.ExceptionConverters.AddRange(...))`, or `services.Configure<RpcServerOptions>(...)`. A
`LightweightRpcServer` gets its options in the constructor, e.g. resolved as `IOptions<RpcServerOptions>`. On the
client side, `services.ConfigureRpcClientExceptionConverters()` adds all `IRpcClientExceptionConverter` services to
the client options.

The converters of a single service can be registered in its service options, with the options of `RegisterService`,
or with dependency injection, e.g.
`services.Configure<RpcServiceOptions<IThermostatService>>(o => o.ExceptionConverters.Add(...))`.

A registered converter applies to all operations, without any attributes. It must be registered on both sides: if
only the server has it, the client gets an `RpcFaultException` with the fault code and the message, instead of the
exception.

### Converters with fault details

To send more than the message, derive from `RpcExceptionConverter<TException, TFault>`:

```csharp
public class TemperatureException : Exception
{
    public TemperatureException(string message, double temperature) : base(message)
    {
        this.Temperature = temperature;
    }

    public double Temperature { get; }
}

public class TemperatureExceptionConverter : RpcExceptionConverter<TemperatureException, TemperatureFault>
{
    public TemperatureExceptionConverter() : base(includeSubTypes: true)
    {
    }

    public override TemperatureException CreateException(string message, TemperatureFault details)
        => new TemperatureException(message, details.Temperature);

    public override RpcFaultException CreateFault(TemperatureException exception)
        => new RpcFaultException<TemperatureFault>(this.FaultCode, exception.Message,
            new TemperatureFault { Temperature = exception.Temperature });
}
```

The fault code is retrieved from the details type, as for `[RpcFault]`, unless it's passed to the base constructor.
The converter declares its fault, so it can be applied with `[RpcFaultConverter(typeof(TemperatureExceptionConverter))]`,
or registered in the options on both sides, without `[RpcFault]`.

### Custom converters

For other conversions, implement `IRpcServerExceptionConverter` and `IRpcClientExceptionConverter` directly, or derive
from `RpcExceptionConverterBase<TException>` for a conversion without details. Implement both interfaces in the same
class when possible: a client converter that also implements `IRpcServerExceptionConverter` provides the fault code
and the details type, so that the client can deserialize the details.

## Which converter is used

On the server side, the converters of the operation are tried first, then the converters of the service, then the
converters registered in the service options, and last the converters registered in the server options. The first
converter that handles the exception creates the fault. On the server side, two converters for the same fault code
on the same level are an error (`RpcDefinitionException`).

On the client side, the declarations and converters of the operation and the service are used first, and then the
converters registered in the client options. The attributes are retrieved both from the client interface and from
its server side definition (`[RpcService(ServerDefinitionType = ...)]`), so a client interface doesn't need to repeat
them.

| The implementation throws | The client gets |
|---|---|
| `RpcFaultException<TFault>`, declared with `[RpcFault]` | `RpcFaultException<TFault>`, with the details |
| `RpcFaultException<TFault>`, not declared | `RpcFaultException`, without the details |
| `RpcFaultException` | `RpcFaultException` |
| An exception with a converter, applied by an attribute or registered on both sides | The exception, created by the converter |
| An exception with a converter that is only registered on the server | `RpcFaultException` |
| Any other exception | `RpcFailureException` |

The same applies to all kinds of operations, including server streaming and client streaming operations. A fault in
a server streaming operation is thrown when the client enumerates the stream, after the items that were sent before
the fault.

## Security considerations

Everything in a fault is sent to the client: the message, and the details. `RpcExceptionConverter<TException>` sends
the message of the exception, so only use it for exceptions whose messages don't contain sensitive information, such
as file paths, connection strings or internal identifiers. For other exceptions, implement a converter that creates
a suitable message, or throw a fault with a message intended for the client.

Exceptions that are not converted never reach the client: the client gets an `RpcFailureException` with a generic
message, and the exception should be logged on the server.
