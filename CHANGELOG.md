# Changelog

## 1.0.0 (release candidate)

The first stable release. Compared to the 1.0 betas on nuget.org, it changes the supported platforms, the
packages and parts of the public API, and the Lightweight discovery protocol.

### Breaking changes

* **Platforms.** The libraries target .NET Framework 4.7.2, .NET 8 and .NET 10. Older .NET Core and .NET Standard
  targets are no longer supported.
* **Grpc.Core removed.** The `SciTech.Rpc.Grpc` package, based on the native Grpc.Core, is no longer provided. Use
  `SciTech.Rpc.NetGrpc` (server) and `SciTech.Rpc.NetGrpc.Client` (client), based on gRPC for .NET.
* **Analyzer.** The RPC analyzer is included in the `SciTech.Rpc` package. The separate `SciTech.Rpc.Analyzer`
  package is deprecated. The analyzer ignores static interface members.
* **Lightweight discovery.** The discovery protocol has been redesigned (see below), and is not compatible with
  the betas: clients and servers must be updated together. `DiscoveredServerEventArgs.DiscoveredServer` is now a
  `DiscoveredServer`, `ServiceDiscoveryTimeout` is replaced by `ServerLostTimeout`, and `FindServicesAsync` returns
  an `IReadOnlyList<DiscoveredService>`.
* **Collections in the public API.** Members that returned `ImmutableArray<T>`, `IImmutableList<T>` or
  `ImmutableArrayList<T>` now return `IReadOnlyList<T>`, documented as immutable, e.g.
  `RpcServerBase.CallInterceptors`, `IRpcServiceDefinitionsProvider.GetRegisteredServiceTypes`, the immutable
  options classes and the discovery client. Code that used `Length` or `IsDefaultOrEmpty` must use `Count`.
  Collection parameters, e.g. of `LocksReleasedEventArgs`, accept any `IEnumerable<T>`.
* **`IRpcServerOptions.ServerProperties`.** A new interface member (see below).
* **`RpcServicePublisher.InitConnectionInfo`** now replaces a connection info with the same server id. Before, it
  was silently ignored, unless the connection info had no server id.
* **`IReadOnlyList<T>.FindIndex`** in SciTech.Core is renamed to `FindReadOnlyIndex`, to avoid ambiguities with
  `IList<T>.FindIndex`.
* **Lightweight error details** are sent under the `scitech_rpc.error_details-bin` header.

### New features

* **Client streaming.** Operations can take an `IAsyncEnumerable<T>` parameter, e.g.
  `Task<int> Upload(string name, IAsyncEnumerable<Sample> samples)`, with both Lightweight and NetGrpc.
* **Lightweight operation numbers.** After the first call of an operation on a connection, it's identified by a
  small number instead of its name, which reduces the size of small calls considerably. It's negotiated per call,
  so it's compatible with older clients and servers, and can be disabled with `LightweightOptions.UseOperationNumbers`,
  e.g. to keep the names visible when analyzing network traffic.
* **Lightweight discovery** finds servers that publish specific singleton services, so a client can e.g. list
  servers with an older version of a service as incompatible, while other kinds of servers are not listed. All
  datagrams fit in a single unfragmented datagram, IPv6 is supported, and servers are only lost after several
  missed responses. See the [protocol specification](docs/lightweight-protocol.md#7-service-discovery).
* **Server properties.** `RpcServerOptions.ServerProperties` are application-defined key/value pairs that describe
  a server, e.g. its version. They're provided by discovery and by `GetServerInfoAsync`.
* **`IRpcChannel.GetServerInfoAsync()`** returns the id, display name, properties and published singleton services
  of the server, over the authenticated connection.
* **Exception converters declare their faults.** A converter with fault details, e.g. derived from
  `RpcExceptionConverter<TException, TFault>`, works when applied with `[RpcFaultConverter]`, or when registered in
  the options on both sides, without also declaring the fault with `[RpcFault]`. Before, the details were dropped and
  the client got a plain `RpcFaultException`. See [Exception handling](docs/exception-handling.md).
* **`[RpcKnownType]`** declares the known types of polymorphic serialization, for all serializers.
* **Lightweight protocol specification**, in [docs/lightweight-protocol.md](docs/lightweight-protocol.md).
* `SilentStreamingCancellation` option, connection priorities in `RpcConnectionManager`, the connection state
  `Connecting`, and the end point and established connection in `IRpcServerContext`.

### Fixes

* **Security:** a Lightweight discovery end point dispatched any operation received in a datagram, so service
  operations could be called over UDP without authentication. Only the discovery operations are now handled, and
  an invalid datagram no longer stops the discovery listener.
* Lightweight discovery works on Linux and with IPv6, and responses no longer get fragmented.
* NetGrpc calls that fail after their deadline are reported as timeouts, and NetGrpc streaming cancellation, event
  errors, message size limits and option propagation are fixed.
* A Lightweight connection is reset when a write fails, and the client receive loop no longer waits for itself
  when closing.
* Proxies are generated in a non-collectible assembly, which fixes crashes on .NET Framework x64.
