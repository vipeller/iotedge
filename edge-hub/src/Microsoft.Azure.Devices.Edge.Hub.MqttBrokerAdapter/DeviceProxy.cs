// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Hub.MqttBrokerAdapter
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Edge.Hub.Core;
    using Microsoft.Azure.Devices.Edge.Hub.Core.Device;
    using Microsoft.Azure.Devices.Edge.Hub.Core.Identity;
    using Microsoft.Azure.Devices.Edge.Util;

    public class DeviceProxy : IDeviceProxy
    {
        public bool IsActive => true;

        public IIdentity Identity => throw new NotImplementedException();

        public Task CloseAsync(Exception ex)
        {
            throw new NotImplementedException();
        }

        public Task<Option<IClientCredentials>> GetUpdatedIdentity()
        {
            throw new NotImplementedException();
        }

        public Task<DirectMethodResponse> InvokeMethodAsync(DirectMethodRequest request)
        {
            throw new NotImplementedException();
        }

        public Task OnDesiredPropertyUpdates(IMessage desiredProperties)
        {
            throw new NotImplementedException();
        }

        public Task SendC2DMessageAsync(IMessage message)
        {
            throw new NotImplementedException();
        }

        public Task SendMessageAsync(IMessage message, string input)
        {
            throw new NotImplementedException();
        }

        public Task SendTwinUpdate(IMessage twin)
        {
            return Task.FromResult(true);
        }

        public void SetInactive()
        {
            throw new NotImplementedException();
        }
    }
}
