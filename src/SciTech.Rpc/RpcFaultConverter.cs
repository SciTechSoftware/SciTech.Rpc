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

using System;

namespace SciTech.Rpc
{
    public class ConvertedFault
    {
        public ConvertedFault(string faultCode, string message)
        {
            this.FaultCode = faultCode ?? throw new ArgumentNullException(nameof(faultCode));
            this.Message = message ?? throw new ArgumentNullException(nameof(message));
            this.Details = null;
        }

        public ConvertedFault(string faultCode, string message, object details)
        {
            this.FaultCode = faultCode ?? throw new ArgumentNullException(nameof(faultCode));
            this.Message = message ?? throw new ArgumentNullException(nameof(message));
            this.Details = details;

        }


        public string FaultCode { get; }

        public string Message { get; }

        public object? Details { get; }
    }
}
