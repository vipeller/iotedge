// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Util.Test.Common
{
    using System.Diagnostics;
    using Xunit;

    // from: https://lostechies.com/jimmybogard/2013/06/20/run-tests-explicitly-in-xunit-net/
    public class RunnableInDebugOnlyAttribute : FactAttribute
    {
        public RunnableInDebugOnlyAttribute()
        {
            if (!Debugger.IsAttached)
            {
                this.Skip = "Only running in interactive mode.";
            }
        }
    }
}