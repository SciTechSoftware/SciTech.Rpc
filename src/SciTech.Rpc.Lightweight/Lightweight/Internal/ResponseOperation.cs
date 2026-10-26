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

namespace SciTech.Rpc.Lightweight.Internal
{
    /// <summary>
    /// Identifies the operation in the response frames of a call, as specified by section 6.4 of
    /// docs/lightweight-protocol.md.
    /// </summary>
    internal readonly struct ResponseOperation
    {
        public static readonly ResponseOperation Empty = new ResponseOperation("", 0);

        private readonly string? name;

        private ResponseOperation(string name, uint number)
        {
            this.name = name;
            this.Number = number;
        }

        /// <summary>
        /// The operation name to echo in the response, or an empty string if the client supports operation numbers.
        /// </summary>
        public string Name => this.name ?? "";

        /// <summary>
        /// The operation number to return to the client, or 0 if no operation number should be returned.
        /// </summary>
        public uint Number { get; }

        /// <summary>
        /// Creates the response operation for a call.
        /// </summary>
        /// <param name="requestFrame">The request frame that started the call.</param>
        /// <param name="operationNumber">The server's operation number for the requested operation, or 0 if the operation
        /// is not known, or if the server doesn't use operation numbers.</param>
        public static ResponseOperation Create(in LightweightRpcFrame requestFrame, uint operationNumber)
        {
            var flags = requestFrame.OperationFlags;
            if ((flags & RpcOperationFlags.HasOperationNumber) != 0)
            {
                // The request was sent by number, so the client already knows the number, and doesn't need the name.
                return Empty;
            }

            if ((flags & RpcOperationFlags.AcceptsOperationNumber) != 0 && operationNumber != 0)
            {
                return new ResponseOperation("", operationNumber);
            }

            // The client doesn't support operation numbers, or the server doesn't use them, so the name is echoed as before.
            return new ResponseOperation(requestFrame.RpcOperation, 0);
        }

        /// <summary>
        /// Gets a response operation that is identical to this one, except that no operation number is returned.
        /// Used for subsequent response frames of a call, after the first frame has returned the number.
        /// </summary>
        public ResponseOperation WithoutNumber() => new ResponseOperation(this.Name, 0);
    }
}
