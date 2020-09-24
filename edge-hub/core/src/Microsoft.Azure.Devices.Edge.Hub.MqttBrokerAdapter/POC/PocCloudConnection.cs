namespace Microsoft.Azure.Devices.Edge.Hub.MqttBrokerAdapter
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Edge.Hub.Core.Cloud;
    using Microsoft.Azure.Devices.Edge.Util;

    public class PocCloudConnection : ICloudConnection
    {
        public PocCloudConnection(PocCloudProxy cloudProxy)
        {
            this.IsActive = true;
            this.CloudProxy = Option.Some(cloudProxy as ICloudProxy);
        }

        public Option<ICloudProxy> CloudProxy { get; }

        public bool IsActive { get; private set; }

        public Task<bool> CloseAsync()
        {
            // Note, probably should do other stuff
            this.IsActive = false;
            return Task.FromResult(true);
        }
    }
}
