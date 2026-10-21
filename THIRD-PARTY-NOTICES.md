# Third-party notices

SciTech.Rpc includes source code based on the following third-party projects.
The original copyright notices are kept in the affected source files.

## Nerdbank.Streams

Used in `src/SciTech.Core/Buffers/OwnedArraySegment.cs` and `src/SciTech.Core/Buffers/ReadOnlySequenceStream.cs`.

https://github.com/AArnott/Nerdbank.Streams

```
MIT License

Copyright (c) Andrew Arnott

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Pipelines.Sockets.Unofficial

Used in `src/SciTech.Rpc.Lightweight/Lightweight/Internal/PipeUri.cs`,
`src/SciTech.Rpc.Lightweight/Lightweight/Server/Internal/AuthenticatedSocketServer.cs` and
`src/SciTech.Rpc.Lightweight/Lightweight/Server/Internal/NamedPipeServer.cs`.

https://github.com/mgravell/Pipelines.Sockets.Unofficial

```
The MIT License (MIT)

Copyright (c) 2018 Marc Gravell

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## AsyncEx

Used in `src/SciTech.Core/Threading/CancellationTokenTaskSource.cs` and `src/SciTech.Core/Threading/TaskExtensions.cs`.
The full MIT license text is included in those files.

https://github.com/StephenCleary/AsyncEx

Copyright (c) 2014 StephenCleary

## gRPC

Test certificates and helpers in `Certs/`, `test/SciTech.Rpc.Tests/TaskExtensions.cs` and some example code are
adapted from gRPC for .NET (https://github.com/grpc/grpc-dotnet), licensed under the
[Apache License, Version 2.0](http://www.apache.org/licenses/LICENSE-2.0).

Copyright 2019 The gRPC Authors
