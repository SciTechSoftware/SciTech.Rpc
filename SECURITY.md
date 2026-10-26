# Security policy

## Reporting a vulnerability

Please don't report security vulnerabilities in public issues.

Report them privately, using [GitHub's private vulnerability reporting](https://github.com/SciTechSoftware/SciTech.Rpc/security/advisories/new),
or by [e-mail](mailto:andreas@scitech.se). Include a description of the issue, the affected versions, and if
possible the steps to reproduce it. We aim to respond within a few working days.

## Supported versions

Security fixes are made for the latest release.

| Version | Supported |
|---|---|
| 1.0.x | Yes |
| Earlier versions, including the 1.0 betas | No |

## Security model

* Connections are only authenticated and encrypted if the end point is configured to require it: TLS or Negotiate
  for Lightweight TCP, and the ASP.NET Core authentication for NetGrpc. A TCP end point without authentication,
  and a named pipe end point, should only be used on trusted networks and machines.
* Lightweight service discovery is unauthenticated by design. Anyone on the local network can find the servers
  and the names of their discoverable singleton services, and can send discovery requests. Discovery requests can
  only reach the discovery operations, never the service operations. Use `AllowDiscovery` to keep services out of
  discovery, and only use discovery results as a pre-filter.
* Server properties are provided by discovery without authentication, so they must not contain secrets.
