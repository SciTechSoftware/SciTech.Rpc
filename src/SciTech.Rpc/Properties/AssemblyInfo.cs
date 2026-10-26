#region Copyright notice and license
// Copyright (c) SciTech Software AB and contributors.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("SciTech.Rpc.Tests")]

// Make internals visible for logging purposes
[assembly: InternalsVisibleTo("SciTech.Rpc.NetGrpc")]
[assembly: InternalsVisibleTo("SciTech.Rpc.NetGrpc.Client")]
[assembly: InternalsVisibleTo("SciTech.Rpc.Lightweight")]
[assembly: InternalsVisibleTo("SciTech.Rpc.DataContract")]
[assembly: InternalsVisibleTo("SciTech.Rpc.Resources")]
