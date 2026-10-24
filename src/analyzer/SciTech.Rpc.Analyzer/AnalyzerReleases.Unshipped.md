; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
RPC1001 | RPC | Error | Duplicate server side operation
RPC1002 | RPC | Error | Client side operation missing in server side definition
RPC1003 | RPC | Error | MarshalByRefObject parameter
RPC1004 | RPC | Error | MarshalByRefObject return value
RPC1005 | RPC | Error | ref/in/out parameter
RPC1006 | RPC | Error | RPC service parameter
