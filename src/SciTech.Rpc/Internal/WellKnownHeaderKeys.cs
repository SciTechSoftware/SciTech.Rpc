#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion


namespace SciTech.Rpc.Internal
{
    public static class WellKnownHeaderKeys
    {
        public const string ErrorMessage = "scitech_rpc.error_message";

        public const string ErrorType = "scitech_rpc.error_type";

        public const string ErrorCode = "scitech_rpc.error_code";

        public const string ErrorDetails = "scitech_rpc.error_details-bin";

        /// <summary>
        /// Serialized <see cref="RpcError"/>. Can be used instead of separate ErrorMessage, ErrorType, ErrorCode, ErrorDetails.
        /// </summary>
        public const string ErrorInfo = "scitech_rpc.error_info-bin";
    }

}
