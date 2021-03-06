// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Test
{
    using System;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Edge.Agent.Core.PlanRunners;
    using Microsoft.Azure.Devices.Edge.Test.Common;
    using Microsoft.Azure.Devices.Edge.Test.Common.Config;
    using Microsoft.Azure.Devices.Edge.Test.Helpers;
    using Microsoft.Azure.Devices.Edge.Util;
    using Microsoft.Azure.Devices.Edge.Util.Test.Common.NUnit;
    using NUnit.Framework;

    [EndToEnd]
    class DeviceWithCustomCertificates : CustomCertificatesFixture
    {
        [Test]
        public async Task TransparentGateway(
            [Values] TestAuthenticationType testAuth,
            [Values(Protocol.Mqtt, Protocol.Amqp)] Protocol protocol)
        {
            CancellationToken token = this.TestToken;

            await this.runtime.DeployConfigurationAsync(token, Context.Current.NestedEdge);

            string leafDeviceId = DeviceId.Current.Generate();

            Option<string> parentId = testAuth == TestAuthenticationType.SasOutOfScope
                ? Option.None<string>()
                : Option.Some(this.runtime.DeviceId);

            LeafDevice leaf = null;
            try
            {
                leaf = await LeafDevice.CreateAsync(
                    leafDeviceId,
                    protocol,
                    testAuth.ToAuthenticationType(),
                    parentId,
                    testAuth.UseSecondaryCertificate(),
                    this.ca,
                    this.IotHub,
                    Context.Current.Hostname.GetOrElse(Dns.GetHostName().ToLower()),
                    token,
                    Option.None<string>(),
                    Context.Current.NestedEdge);
            }
            catch (Exception) when (!parentId.HasValue)
            {
                return;
            }

            if (!parentId.HasValue)
            {
                Assert.Fail("Expected to fail when not in scope.");
            }

            Assert.NotNull(leaf);

            await TryFinally.DoAsync(
                async () =>
                {
                    DateTime seekTime = DateTime.Now;
                    await leaf.SendEventAsync(token);
                    await leaf.WaitForEventsReceivedAsync(seekTime, token);
                    await leaf.InvokeDirectMethodAsync(token);
                },
                async () =>
                {
                    await leaf.DeleteIdentityAsync(token);
                });
        }
    }
}
