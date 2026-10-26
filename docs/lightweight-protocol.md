# SciTech.Rpc Lightweight protocol

This document specifies the frame format used by the Lightweight transport (`SciTech.Rpc.Lightweight`). It
describes protocol version 1, including the *operation number* extension (section 6), which is new in
SciTech.Rpc 1.0.

The protocol is also implemented by an embedded C++ server, SciTech.Rpc.Cpp. That implementation is proprietary
and not part of this repository. It is mentioned here where its behaviour differs from the C# implementation.

The key words MUST, MUST NOT, SHOULD, SHOULD NOT and MAY are used as described in RFC 2119.

## 1. Overview

A Lightweight connection is a bidirectional byte stream (TCP, optionally with TLS or Negotiate authentication,
named pipes, or an in-process pipe) carrying a sequence of frames in each direction. The client starts calls by
sending request frames; the server answers with response frames carrying the same message number. Several calls
can be outstanding on one connection at the same time, and their frames can be interleaved.

Service discovery uses the same frames over UDP (section 7).

## 2. Encoding primitives

| Type | Encoding |
|---|---|
| `int16`, `int32` | Little-endian, two's complement. |
| `varint` | Unsigned 32-bit integer, encoded as a Protobuf-style base-128 varint (7 bits per byte, least significant group first, high bit set on all but the last byte). At most 5 bytes. A varint that doesn't terminate within 5 bytes is invalid. |
| `String` | `varint` byte length, followed by that many bytes of UTF-8. No terminator. An empty string is the single byte `0x00`. |
| `Bytes` | `varint` byte length, followed by that many bytes. |

## 3. Frame layout

| Offset | Field | Type | Description |
|---|---|---|---|
| 0 | `Version` | `int16` | Protocol version. Always `1`. |
| 2 | `FrameType` | `int16` | Frame type (section 4). |
| 4 | `FrameLength` | `int32` | Total length of the frame in bytes, including this header and the payload. |
| 8 | `MessageNumber` | `int32` | Identifies the call the frame belongs to. |
| 12 | `PayloadLength` | `int32` | Length of `Payload` in bytes. |
| 16 | `Operation` | `String` | Name of the RPC operation. May be empty (see section 5). |
| … | `OperationFlags` | `varint` | Bit flags (section 3.1). |
| … | `OperationNumber` | `varint` | Operation number (section 6). **Present only if `OperationFlags` has `HasOperationNumber` set.** |
| … | `Timeout` | `varint` | Operation timeout in milliseconds. `0` means no timeout. |
| … | `HeaderPairsCount` | `varint` | Number of header pairs that follow. |
| … | `HeaderPairs` | pair[] | `HeaderPairsCount` pairs of `Key` (`String`) and `Value` (`Bytes`). |
| … | `Payload` | byte[] | `PayloadLength` bytes, for example a serialized request or response. |

The fixed part of the header is 16 bytes. The length of the variable part is
`FrameLength - 16 - PayloadLength`.

Rules:

- A receiver MUST reject a frame whose `Version` is not `1`. This is not recoverable: the receiver can't know
  how to parse the rest of the stream, so the connection is closed.
- `FrameLength` MUST NOT be negative. Each receiver has a maximum frame length (64 KiB by default). A frame that
  exceeds it either closes the connection, or, if skipping large frames is enabled, is skipped after its header
  has been read. A server that skips a request frame answers with an `ErrorResponse` with error code
  `SizeLimitExceeded`.
- Header values are opaque bytes. Text values are UTF-8. Keys ending in `-bin` hold binary values (as in gRPC).
- The payload is produced by the serializer configured for the service (JSON by default, or for example
  Protobuf). The serializer is not negotiated; both sides must use the same one.

### 3.1 Operation flags

| Value | Name | Sent in | Meaning |
|---|---|---|---|
| `0x0001` | `CanCancel` | Requests | The client may cancel the call with a `CancelRequest`. |
| `0x0002` | `AcceptsOperationNumber` | Requests | The client supports operation numbers (section 6). Doesn't add a field. |
| `0x0004` | `HasOperationNumber` | Requests, responses | The `OperationNumber` field is present (section 6). |

Receivers MUST ignore flag bits they don't know. A flag that changes the frame layout, such as
`HasOperationNumber`, MUST only be sent to a peer that is known to understand it.

## 4. Frame types

| Value | Name | Sender | Payload |
|---|---|---|---|
| 0 | `UnaryRequest` | Client | Serialized request. |
| 1 | `UnaryResponse` | Server | Serialized response. |
| 2 | `ServerStreamingRequest` | Client | Serialized request. |
| 3 | `StreamingResponse` | Server | One serialized response stream item. |
| 4 | `StreamingEnd` | Both | Empty. Ends a response stream (server) or a request stream (client). |
| 5 | `CancelRequest` | Client | Empty. |
| 6 | `CancelResponse` | Server | Empty. |
| 7 | `TimeoutResponse` | Server | Empty. |
| 8 | `ErrorResponse` | Server | Empty. The error is in the headers (section 5.5). |
| 9 | `ServiceDiscoveryRequest` | – | Reserved, not used. Discovery uses unary frames (section 7). |
| 10 | `ServiceDiscoveryResponse` | – | Reserved, not used. |
| 11 | `ConnectionRequest` | Client | Empty. TCP only (section 5.1). |
| 12 | `ConnectionResponse` | Server | Empty. TCP only (section 5.1). |
| 13 | `StreamingData` | Client | One serialized request stream item. New in 1.0. |
| 14 | `ClientStreamingRequest` | Client | Serialized request (the non-stream parameters). New in 1.0. |

New frame types MUST be appended, so that the values of the existing types never change.

## 5. Call flows

### Message numbers

The client assigns the `MessageNumber` of each call. It MUST be non-zero and unique among the calls that are
outstanding on the connection. Every frame of a call, in both directions, carries the call's message number.
Connection frames use `0`.

### `Operation` field

Request frames (`UnaryRequest`, `ServerStreamingRequest` and `ClientStreamingRequest`) identify the operation,
either by name in `Operation` or by number (section 6). The name is the service name and the operation name,
separated by a dot, for example `Thermostat.PidRegulatorService.GetPidState`.

All other frames are matched to their call by message number. The server ignores `Operation` in
`StreamingData`, `StreamingEnd` and `CancelRequest` frames, and clients SHOULD send it empty there. In response
frames the server echoes the request's operation name, except as section 6.4 allows. The C# client uses the
echoed name in error messages, and the discovery client uses it to tell the discovery operations apart
(section 7).

### 5.1 Connection handshake (TCP only)

On a TCP connection, the client first sends a `ConnectionRequest`, unencrypted, with the header
`scitech_rpc.authentication_scheme` listing the schemes it supports, separated by `;`. The server answers with a
`ConnectionResponse` whose `scitech_rpc.authentication_scheme` header names the selected scheme. If none
matched, the value is empty and the server closes the connection. Then the selected authentication (for
example TLS or Negotiate) runs, and all later frames go over the authenticated stream. Other transports have no
handshake.

### 5.2 Unary call

Client: `UnaryRequest`. Server: `UnaryResponse`, or `ErrorResponse`, `CancelResponse` or `TimeoutResponse`.

### 5.3 Server-streaming call

Client: `ServerStreamingRequest`. Server: zero or more `StreamingResponse` frames, then `StreamingEnd`, or an
`ErrorResponse`, `CancelResponse` or `TimeoutResponse` instead of `StreamingEnd`.

### 5.4 Client-streaming call

Client: `ClientStreamingRequest`, then zero or more `StreamingData` frames, then `StreamingEnd`. Server:
`UnaryResponse`, or `ErrorResponse`, `CancelResponse` or `TimeoutResponse`. The server can answer before the
client has finished the request stream, for example with an error.

### 5.5 Cancellation, timeouts and errors

- If the request had `CanCancel` set, the client may send a `CancelRequest` with the call's message number. If
  the operation is cancelled, the server answers with `CancelResponse`.
- If `Timeout` is non-zero, the server cancels the operation when the timeout elapses and answers with
  `TimeoutResponse`. (The C++ server currently ignores the timeout.)
- An `ErrorResponse` carries the error in headers. If the operation's serializer is known, the header
  `scitech_rpc.error_info-bin` holds the serialized `RpcError`. Otherwise the error is in the headers
  `scitech_rpc.error_type`, `scitech_rpc.error_code`, `scitech_rpc.error_message` and, optionally,
  `scitech_rpc.error_details-bin`. Protocol-level failures use the error type `SciTech.Rpc.RpcFailureError`, with
  an `RpcFailure` name as the error code, for example `RemoteDefinitionError` for an unknown operation.

### 5.6 Built-in operations

Servers implement these operations in addition to the services they publish. They are ordinary unary calls, and
their payloads use the server's default serializer. They're not published services, so discovery doesn't list
them.

| Operation | Request | Response |
|---|---|---|
| `SciTech.Rpc.RpcService.QueryServices` | `RpcObjectRequest`: the object id. | `RpcServicesQueryResponse`: the names of the services the object implements. |
| `SciTech.Rpc.RpcService.GetServerInfo` | `RpcObjectRequest`: an empty object id. | `RpcResponse` with an `RpcServerInfoResponse`. New in 1.0. |

`RpcServerInfoResponse` describes the server, as discovery does (section 7), but over the authenticated
connection, so a client can check whether a server it connected to by host name or address is compatible:

| Property | Description |
|---|---|
| `ConnectionInfo` | `DisplayName`, `HostUrl` and `ServerId` of the server. |
| `Properties` | The server properties (section 7.4). |
| `Services` | The names of the singleton services the server publishes, including those that don't allow discovery. The services of an object are provided by `QueryServices`. |

## 6. Operation numbers

### 6.1 Purpose

Operation names are typically 30–80 bytes, and every request carries one. Responses have so far echoed it back
too. For small, frequent calls, for example to the embedded C++ server, the names are most of the traffic.
Operation numbers let a client name an operation once per connection and then refer to it with a one- or
two-byte number.

The extension is negotiated call by call, through flags, so it needs no version change or handshake. It works
on all transports, and a peer that doesn't implement it never sees an operation number.

### 6.2 Server: assigning numbers

- The server assigns each operation a non-zero number. A number MUST refer to the same operation for at least
  the lifetime of the connection. In practice the server assigns numbers for its own lifetime, for example
  from a counter when the operation is first requested, or from the operation's index in a stub table.
- Numbers below 128 encode as one byte. Servers SHOULD keep them small.
- The client treats the number as opaque. It MUST NOT assume numbers are stable across connections, or equal
  between servers.

### 6.3 Client: sending requests

The client keeps a map from operation name to number **for each connection**. The map MUST be discarded when the
connection is closed or reset. A reconnect may reach a restarted server, or a device with new firmware, where a
stale number would call a different operation.

For each request frame:

- **Number known:** set `HasOperationNumber`, write the number in `OperationNumber`, and leave `Operation`
  empty. Don't set `AcceptsOperationNumber`.
- **Number not known:** write the name in `Operation` and set `AcceptsOperationNumber`. Leave
  `HasOperationNumber` clear.

Until the first response arrives, concurrent requests for the same operation are all sent by name. That is
allowed.

Only request frames carry operation numbers. `StreamingData`, `StreamingEnd` and `CancelRequest` frames MUST NOT
set `HasOperationNumber`.

### 6.4 Server: handling requests and responses

- A request with `HasOperationNumber` set MUST have an empty `Operation`. If it doesn't, the server answers with
  an `ErrorResponse` with error code `InvalidData`.
- If the number is unknown, the server answers with an `ErrorResponse` with error code `RemoteDefinitionError`,
  as for an unknown name. The client SHOULD then discard its map for the connection. It MUST NOT resend the call
  automatically (see section 8).
- If a request by name has `AcceptsOperationNumber` set and the operation exists, the server SHOULD set
  `HasOperationNumber` and the number in the **first response frame** of the call, whatever its type. It MAY
  include the number in later response frames of the call too. Response frames of other calls, and calls
  requested by number, don't carry a number.
- In responses to a call that was requested by number, or for which the server returns a number, the server
  SHOULD leave `Operation` empty. For other calls it MUST echo the operation name as before, because older
  clients and the discovery client depend on it.
- The server MUST NOT set `HasOperationNumber` in a response unless the request had `AcceptsOperationNumber`
  set. Older clients would misread the frame.

When a response frame has `HasOperationNumber` set, the client records the number for the operation of the
matching call. Clients that implement the extension MUST NOT depend on the `Operation` field of responses.

### 6.5 Compatibility

| Client | Server | Result |
|---|---|---|
| With extension | With extension | Requests by name until the first response, then by number. |
| With extension | Without | The server ignores `AcceptsOperationNumber` and never returns a number, so the client keeps sending names. |
| Without | With extension | The client never sets `AcceptsOperationNumber`, so the server never adds the field. |

Pre-1.0 clients and servers (SciTech.Rpc 1.0 betas) ignore unknown flags: the C# server only tests `CanCancel`,
and the C++ server doesn't test flags.

Implementations SHOULD let the extension be disabled, since operation names make frames much easier to identify
when analysing network traffic. A client with the extension disabled behaves as a client without it, and a server
with it disabled behaves as a server without it, so either side can turn it off for the connection. In the C#
implementation this is `LightweightOptions.UseOperationNumbers`.

### 6.6 Example

A request for `Thermostat.PidRegulatorService.GetPidState` with a 5 s timeout and no headers, and its response
(header bytes only, payloads excluded):

| Frame | By name | By number |
|---|---|---|
| Request | 16 + 43 (name) + 1 (flags) + 2 (timeout) + 1 (header count) = **63** | 16 + 1 + 1 + 1 (number) + 2 + 1 = **22** |
| Response | 16 + 43 (echoed name) + 1 + 1 + 1 = **62** | 16 + 1 + 1 + 1 + 1 = **20** |

The first response, which assigns the number, is 21 bytes.

## 7. Service discovery

### 7.1 Overview

Discovery lets a client find the servers on the local network, see which of the singleton services it needs
each server publishes, and read a few properties of each server, for example its product version.

A client can name the services it's interested in. Only servers that publish at least one of them answer, and
the answer says which. The client decides what to do with a server that only publishes some of them. For
example, a client that requests both `Thermostat.DeviceManagerService` and `Thermostat.DeviceManagerService2`
can show a server that only publishes the first one as incompatible, while other kinds of servers don't answer
at all. A client that names no services finds all servers.

Discovery is a pre-filter. It is unauthenticated, and clients often connect by host name or address without
it, so the authoritative compatibility check is made on the connection.

Each datagram carries a single frame, and each request gets at most one response:

| Operation | Sent | Purpose |
|---|---|---|
| `SciTech.Rpc.Discovery.FindServers` | Multicast | Find servers, their properties and the requested services. |
| `SciTech.Rpc.Discovery.GetServices` | Multicast, answered by one server | List all discoverable services of a server, one page at a time. |

The request is a `UnaryRequest` frame, and the response a `UnaryResponse` frame with the same message number,
sent to the address and port the request came from.

`GetServices` is multicast, although only one server answers it. All discovery servers on a host listen on the
same port, and a unicast datagram is only delivered to one of them.

### 7.2 Transport

- Multicast groups: `239.255.250.129` (IPv4) and `ff18::732` (IPv6). Port: `39159`.
- A server listens on each multicast-capable, non-loopback interface, separately for IPv4 and IPv6, and joins
  the group of the address family on that interface. A client sends on each such interface and address family
  to the matching group, and receives the responses on the same socket. A client doesn't need to join the
  group.
- Multicast is sent with the default hop limit (TTL) of 1, so discovery only reaches the local network.

### 7.3 Security

Datagrams are unauthenticated, and anyone who can reach the port can send them.

- The server MUST only dispatch the discovery operations through datagrams, never the ordinary service
  operations. The discovery operations are in turn only available through datagrams. Unknown operations, other
  frame types and invalid datagrams are ignored, without a response, and MUST NOT stop the server from handling
  later datagrams.
- The server MUST ignore a datagram shorter than **512 bytes** or longer than **1200 bytes**. Clients pad
  their requests with zero bytes after the frame to reach 512 bytes. Since a response is at most 1200 bytes, a
  spoofed request can't make the server send much more data than it received.
- Discovery frames MUST NOT set `AcceptsOperationNumber` or `HasOperationNumber`. A server MUST echo the
  operation name in responses, since it identifies the response (section 7.5).

### 7.4 Payloads

Payloads are UTF-8 JSON objects. Names are case sensitive. Receivers MUST ignore unknown properties, so new
ones can be added later. A missing property has its default value (`null`, `0` or empty). Writers SHOULD NOT
escape non-ASCII characters, to keep the size limits meaningful. GUIDs are written in the
`xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` format.

**`FindServers` request**

| Property | Type | Description |
|---|---|---|
| `ClientId` | GUID | Chosen by the client for each discovery run. Echoed in the response. |
| `Services` | string[] | Optional. Names of the singleton services the client is interested in. The server considers at most the first 32. If any are given, a server that publishes none of them as discoverable singletons MUST NOT answer. If `Services` is missing or empty, every server answers. |

**`FindServers` response**

| Property | Type | Description |
|---|---|---|
| `ClientId` | GUID | From the request. |
| `ConnectionInfo` | object | `DisplayName`, `HostUrl` and `ServerId` of the server's suggested connection, as in `RpcConnectionInfo`. |
| `Properties` | object | Server properties: string keys and string values, defined by the application. |
| `Matches` | int[] | Zero-based indices into the request's `Services` of the services the server publishes as discoverable singletons, in ascending order. Not empty if the request had `Services`. |
| `ServiceCount` | int | Number of discoverable singleton services. |
| `ServicesHash` | string | Hash of the discoverable singleton services (section 7.6). |

**`GetServices` request**

| Property | Type | Description |
|---|---|---|
| `ClientId` | GUID | From the client's discovery run. |
| `ServerId` | GUID | The server to answer. Other servers MUST NOT answer. |
| `Offset` | int | Index of the first service to return. |

**`GetServices` response**

| Property | Type | Description |
|---|---|---|
| `ClientId` | GUID | From the request. |
| `ServerId` | GUID | The server's id. |
| `ServiceCount` | int | Number of discoverable singleton services. |
| `ServicesHash` | string | Hash of the discoverable singleton services. |
| `Offset` | int | From the request. |
| `Services` | string[] | The services from `Offset` on, in the order of section 7.6, as many as fit in the response. Empty if `Offset` is at or beyond the end. |

### 7.5 Client behaviour

- The client sends `FindServers` periodically (the C# client: every 2 seconds) for as long as discovery runs.
  It matches responses by `ClientId` and identifies servers by `ConnectionInfo.ServerId`. Responses from the
  same server on several interfaces or address families are combined.
- A server is lost when it hasn't responded for a timeout that spans several requests (the C# client: 10
  seconds), so a single lost datagram doesn't make servers come and go.
- For a TCP connection URL, the client replaces the host of `HostUrl` with the address the response came from,
  since the server may suggest a host name the client can't resolve. An IPv6 link-local address needs a zone
  index, which doesn't work well in a URL, so then the suggested host is kept. A connection URL derived from an
  address is preferred over a suggested one.
- To list all services of a server, the client sends `GetServices` requests for that server on the interface and
  address family that the server answered on, starting at offset 0 and continuing at `Offset` plus the number of
  services received, until it has `ServiceCount` services. A request that isn't answered is resent. If
  `ServicesHash` changes between pages, the client starts over. It only needs to fetch the list again when the
  `ServicesHash` of a `FindServers` response changes. A client that only needs specific services uses
  `Services` and `Matches` instead.

### 7.6 Server behaviour and limits

- The discoverable singleton services are the published singletons whose service allows discovery, and whose
  name is at most 256 bytes. They are sorted by the bytes of their UTF-8 names.
- `ServicesHash` is the 64-bit FNV-1a hash of the sorted UTF-8 names, each followed by a `0x0A` byte, written
  as 16 lowercase hexadecimal digits. The client compares it for equality only.
- Responses SHOULD NOT exceed 1200 bytes, the largest UDP payload that won't be fragmented on IPv6 networks. This
  follows from these limits, which a server SHOULD enforce when it is configured:
  - `DisplayName`: at most 128 bytes. `HostUrl`: at most 256 bytes.
  - Server properties: at most 8 entries, with at most 256 bytes for all keys and values together.
  - A `GetServices` response contains as many names as fit within 1200 bytes, but at least one.

### 7.7 Example

A `FindServers` request from a client that needs the device manager service, and the response of a server
that publishes it (payloads only):

```json
{"ClientId":"6f1c1f60-30c2-4a6b-9d63-3c0b4c4f2a51","Services":["Thermostat.DeviceManagerService"]}
```

```json
{"ClientId":"6f1c1f60-30c2-4a6b-9d63-3c0b4c4f2a51",
 "ConnectionInfo":{"DisplayName":"lab-pc-07","HostUrl":"lightweight.tcp://lab-pc-07:50051/","ServerId":"0b8e8a3e-5d0c-4f43-9a51-7f6b2f0f9c11"},
 "Properties":{"product":"Thermostat","version":"4.2.1-alpha.3","channel":"alpha"},
 "Matches":[0],"ServiceCount":5,"ServicesHash":"9f2b6c0d1e4a7735"}
```

## 8. Open questions

- **Discovery on networks that drop multicast.** Switches that snoop IGMP without a querier on the network stop
  forwarding the group after a few minutes. Servers could rejoin the group periodically, or IPv4 discovery could
  fall back to subnet broadcast.
- **DNS-SD.** Discovery through mDNS/DNS-SD could be added after 1.0 as an alternative. Server properties map
  directly to TXT records.
- **Automatic retry after an unknown number.** Nothing was executed, so the client could resend the call by
  name. Since numbers are stable for the connection, this can only happen through a bug, so for now the error is
  just reported.
- **`Version`.** It stays `1`, because all changes so far are negotiated by flags. A future change that can't be
  negotiated per call would need a version handshake, and only TCP has one today (section 5.1).
