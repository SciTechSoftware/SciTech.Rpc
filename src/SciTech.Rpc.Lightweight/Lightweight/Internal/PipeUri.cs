#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//

// Based on Pipelines.Sockets.Unofficial.SocketServer (https://github.com/mgravell/Pipelines.Sockets.Unofficial)
//
// Copyright (c) 2018 Marc Gravell
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.
#endregion

using SciTech.Rpc.Lightweight.Client;
using SciTech.Text;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
#if COREFX
    using Microsoft.Win32.SafeHandles;
    #if NET5_0_OR_GREATER
        using System.Runtime.Versioning;
    #endif
#endif

namespace SciTech.Rpc.Lightweight.Internal
{
    /// <summary>
    /// Disposable wrapper for a pipe name that is stored in a memory mapped file.
    /// </summary>
    internal class PipeNameHolder : IDisposable
    {
        private MemoryMappedFile? mappedFile;

        internal PipeNameHolder(MemoryMappedFile? mappedFile, string pipeName)
        {
            this.mappedFile = mappedFile;
            this.PipeName = pipeName;
        }

        internal string PipeName { get; private set; }

        public void Dispose()
        {
            this.mappedFile?.Dispose();
            this.mappedFile = null;
            this.PipeName = "";
        }
    }

    /// <summary>
    /// Helper class to retrieve the pipe name for a named pipe binding. Inspired by the WCF NamedPipeBinding implementation.
    /// </summary>
    internal static class PipeUri
    {
        internal static PipeNameHolder CreatePipeName(Uri uri)
        {
            string path = uri.AbsolutePath;

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                // On Windows, a unique pipe name is created and stored in a memory mapped file.
                // This prevents remote machines from connecting to the pipe (by hiding the name).

                bool useGlobal = true;
                MemoryMappedFile mappedFile;
                while (true)
                {
                    string sharedMemoryName = CreateSharedMemoryName(path, useGlobal);
                    try
                    {
#if COREFX
                        mappedFile = MemoryMappedFile.CreateNew(sharedMemoryName, Marshal.SizeOf<PipeNameData>(), MemoryMappedFileAccess.ReadWrite);
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            CreateDefaultSecurity().PersistHandle(mappedFile.SafeMemoryMappedFileHandle);
                        }
#else
                        mappedFile = MemoryMappedFile.CreateNew(
                            sharedMemoryName, Marshal.SizeOf<PipeNameData>(), MemoryMappedFileAccess.ReadWrite,
                            MemoryMappedFileOptions.None, CreateDefaultSecurity(), HandleInheritability.None);
#endif

                        break;
                    }
                    catch (Exception e)
                    {
                        if (!useGlobal || CanCreateAnyGlobal())
                        {
                            throw new RpcFailureException(RpcFailure.AddressInUse, $"A named pipe server already exists for the URI '{uri}'", e);
                        }
                    }

                    Debug.Assert(useGlobal);
                    // Try again in the local namespace.
                    useGlobal = false;
                }

                string pipeName = CreateAndStorePipeName(mappedFile);
                return new PipeNameHolder(mappedFile, pipeName);
            }
            else
            {
                // On Unix pipes are always local, so we can just use the shared name directly (and memory mapped
                // files cannot be named)
                return new PipeNameHolder(null, CreateSharedMemoryName(path, null));
            }

        }

        internal static string CreateSharedMemoryName(string absolutePath, bool? useGlobal)
        {
            byte[] hash;
            using (var hashAlgorithm = SHA256.Create())
            {
                hash = hashAlgorithm.ComputeHash(Encoding.UTF8.GetBytes(absolutePath));
            }

            var scheme = WellKnownRpcSchemes.LightweightPipe;
            // Cannot use Convert.ToBase64String, since it may include invalid file name chars.
            string sharedMemoryName = $"{scheme}.{Base32Encoder.Default.Encode(hash)}";

            if (useGlobal != null && Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                return useGlobal.Value ? $"Global\\{sharedMemoryName}" : $"Local\\{sharedMemoryName}";
            }

            return sharedMemoryName;
        }

        // UWP support not yet implemented.
        // internal const string PipeLocalPrefix = @"\\.\pipe\Local\";

        /// <summary>
        /// Looks up the pipe name for the  specified <paramref name="uri"/>. The pipe name is expected
        /// to be stored (by the server) in a memory mapped file with a name created by <see cref="CreateSharedMemoryName(string, bool?)"/> .
        /// Currently this implementation will not support UWP apps on Windows.
        /// </summary>
        /// <param name="uri"></param>
        /// <returns></returns>
        internal static string LookupPipeName(Uri uri)
        {
            var path = uri.AbsolutePath;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // See CreatePipeName
                bool useGlobal = true;
                while (true)
                {
                    string sharedMemoryName = CreateSharedMemoryName(path, useGlobal);

                    try
                    {
                        using var sharedMemory = MemoryMappedFile.OpenExisting(sharedMemoryName, MemoryMappedFileRights.Read);
                        string pipeName = GetPipeName(sharedMemory);

                        // Note, GetPipeName will return an empty string if name cannot be retrieved, but
                        // since the memory mapped file exists that's probably just a race condition, so 
                        // it doesn't make sense to try other memory mapped file names.
                        return pipeName;
                    }
#pragma warning disable CA1031 // Do not catch general exception types
                    catch (Exception)
                    {
                    }
#pragma warning restore CA1031 // Do not catch general exception types

                    if (useGlobal)
                    {
                        // Let's try in the local namespace
                        useGlobal = false;
                    }
                    else
                    {
                        // Not found in the local or global namespace, let's 
                        // give up.
                        break;
                    }
                }
                return "";
            }

            // Unix
            return CreateSharedMemoryName(path, null);

        }

        private static bool CanCreateAnyGlobal()
        {
            bool canCreateAnyGlobal = false;
            try
            {
                using (MemoryMappedFile.CreateNew($"Global\\{Guid.NewGuid()}", Marshal.SizeOf<PipeNameData>(), MemoryMappedFileAccess.ReadWrite)) { }

                canCreateAnyGlobal = true;
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception)
            {
            }
#pragma warning restore CA1031 // Do not catch general exception types

            return canCreateAnyGlobal;
        }

        private static string CreateAndStorePipeName(MemoryMappedFile mappedFile)
        {
            string pipeName;
            using (var va = mappedFile.CreateViewAccessor(0, Marshal.SizeOf<PipeNameData>(), MemoryMappedFileAccess.Write))
            {
                var nameData = new PipeNameData { pipeGuid = Guid.NewGuid() };
                // Write in two-steps, to make sure that the pipeGuid is correctly written 
                // before the isInitialized flags. I assume that there will be a memory barrier
                // between the writes.
                va.Write(0, ref nameData);
                nameData.isInitialized = true;
                va.Write(0, ref nameData);

                pipeName = nameData.pipeGuid.ToString();
            }

            return pipeName;
        }

        private static string GetPipeName(MemoryMappedFile sharedMemory)
        {
            using (var va = sharedMemory.CreateViewAccessor(0, Marshal.SizeOf<PipeNameData>(), MemoryMappedFileAccess.Read))
            {
                va.Read(0, out PipeNameData pipeNameData);
                if (pipeNameData.isInitialized)
                {
                    return pipeNameData.pipeGuid.ToString();
                }
            }

            return "";
        }


        [StructLayout(LayoutKind.Sequential)]
        private struct PipeNameData
        {
            public bool isInitialized;

            public Guid pipeGuid;
        }


#if COREFX
#if NET5_0_OR_GREATER
        [SupportedOSPlatform("windows")]
#endif
        private class MemoryMappedFileSecurity : ObjectSecurity<MemoryMappedFileRights>
        {
            public MemoryMappedFileSecurity()
                : base(false, ResourceType.KernelObject)
            { }

            [System.Security.SecuritySafeCritical]
            internal MemoryMappedFileSecurity(SafeMemoryMappedFileHandle safeHandle, AccessControlSections includeSections)
                : base(false, ResourceType.KernelObject, safeHandle, includeSections)
            { }

            [System.Security.SecuritySafeCritical]
            internal void PersistHandle(SafeHandle handle)
            {
                Persist(handle);
            }
        }
#endif

#if NET5_0_OR_GREATER
        [SupportedOSPlatform("windows")]
#endif
        private static MemoryMappedFileSecurity CreateDefaultSecurity()
        {
            var sec = new MemoryMappedFileSecurity();
            // Deny network access (actually unnecessary, since the memory mapped file cannot be accessed over the network)
            sec.AddAccessRule(new AccessRule<MemoryMappedFileRights>(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), MemoryMappedFileRights.FullControl, AccessControlType.Deny));
            // Allow everyone on the machine to connect
            sec.AddAccessRule(new AccessRule<MemoryMappedFileRights>(new SecurityIdentifier(WellKnownSidType.WorldSid, null), MemoryMappedFileRights.Read, AccessControlType.Allow));
            // The current user should have full control.
            if (WindowsIdentity.GetCurrent().User is SecurityIdentifier userId)
            {
                sec.AddAccessRule(new AccessRule<MemoryMappedFileRights>(userId, MemoryMappedFileRights.FullControl, AccessControlType.Allow));
            }
            return sec;
        }
    }

}
