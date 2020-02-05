// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Util.Test.Common
{
    using System;
    using Xunit.Sdk;

    [TraitDiscoverer("Microsoft.Azure.Devices.Edge.Util.Test.Common.ScenarioDiscoverer", "Microsoft.Azure.Devices.Edge.Util.Test.Common")]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class ScenarioAttribute : Attribute, ITraitAttribute
    {
    }
}