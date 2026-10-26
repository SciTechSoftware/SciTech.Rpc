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

Service discovery uses the same frames over UDP multicast (section 7).

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
- In responses to a call whose request had `AcceptsOperationNumber` or `HasOperationNumber` set, the server
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

### 6.6 Example

A request for `Thermostat.PidRegulatorService.GetPidState` with a 5 s timeout and no headers, and its response
(header bytes only, payloads excluded):

| Frame | By name | By number |
|---|---|---|
| Request | 16 + 43 (name) + 1 (flags) + 2 (timeout) + 1 (header count) = **63** | 16 + 1 + 1 + 1 (number) + 2 + 1 = **22** |
| Response | 16 + 43 (echoed name) + 1 + 1 + 1 = **62** | 16 + 1 + 1 + 1 + 1 = **20** |

The first response, which assigns the number, is 21 bytes.

## 7. Service discovery

Discovery sends one frame per UDP multicast datagram. The client multicasts a `UnaryRequest` for the operation
`SciTech.Rpc.ServiceDiscovery.GetPublishedSingletons`, and each server answers with a `UnaryResponse` to the
sender. The server handles the request in its normal pipeline, as an ordinary operation. The client tells the
responses apart by the echoed `Operation` name (`GetPublishedSingletons`, or the reserved
`SciTech.Rpc.ServiceDiscovery.GetConnectionInfo`). Payloads use the discovery serializer.

Discovery is connectionless and needs the echoed name, so it never uses operation numbers: discovery requests
MUST NOT set `AcceptsOperationNumber` or `HasOperationNumber`.

Discovery is expected to change before the 1.0 release (section 8).

## 8. Open questions

- **Service discovery.** Discovery needs improvements before 1.0, and they may change the discovery frames and
  operations described in section 7.
- **Automatic retry after an unknown number.** Nothing was executed, so the client could resend the call by
  name. Since numbers are stable for the connection, this can only happen through a bug, so for now the error is
  just reported.
- **`Version`.** It stays `1`, because all changes so far are negotiated by flags. A future change that can't be
  negotiated per call would need a version handshake, and only TCP has one today (section 5.1).
