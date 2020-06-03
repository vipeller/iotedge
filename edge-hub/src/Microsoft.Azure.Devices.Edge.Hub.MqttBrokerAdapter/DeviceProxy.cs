// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Hub.MqttBrokerAdapter
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Edge.Hub.Core;
    using Microsoft.Azure.Devices.Edge.Hub.Core.Device;
    using Microsoft.Azure.Devices.Edge.Hub.Core.Identity;
    using Microsoft.Azure.Devices.Edge.Util;
    using Microsoft.Azure.Devices.Edge.Util.Concurrency;
    using Microsoft.Extensions.Logging;

    public class DeviceProxy : IDeviceProxy
    {
        readonly AtomicBoolean isActive;
        readonly IIdentity identity;
        readonly ITwinHandler twinHandler;
        
        public delegate DeviceProxy Factory(IIdentity identity);

        public DeviceProxy(IIdentity identity, ITwinHandler twinHandler)
        {
            this.identity = identity;
            this.twinHandler = twinHandler;
            this.isActive = new AtomicBoolean(true);

            Events.Created(this.identity);
        }

        public bool IsActive => this.isActive.Get();

        public IIdentity Identity => this.identity;

        public Task CloseAsync(Exception ex)
        {
            if (this.isActive.GetAndSet(false))
            {
                // Fixme: figure out how to close it (how to tell the broker)
                Events.Close(this.Identity);
            }

            return TaskEx.Done;
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
            Events.SendingTwinUpdate(this.Identity);
            return this.twinHandler.SendTwinUpdate(twin, this.identity);
        }

        public void SetInactive()
        {
            this.isActive.Set(false);
            Events.SetInactive(this.identity);
        }

        static class Events
        {
            const int IdStart = MqttBridgeEventIds.DeviceProxy;
            static readonly ILogger Log = Logger.Factory.CreateLogger<DeviceProxy>();

            enum EventIds
            {
                Created = IdStart,
                Close,
                SetInactive,
                SendingTwinUpdate
            }

            public static void Created(IIdentity identity) => Log.LogInformation((int)EventIds.Created, $"Created device proxy for {identity.IotHubHostName}/{identity.Id}");
            public static void Close(IIdentity identity) => Log.LogInformation((int)EventIds.Close, $"Closed device proxy for {identity.IotHubHostName}/{identity.Id}");
            public static void SetInactive(IIdentity identity) => Log.LogInformation((int)EventIds.Close, $"Inactivated device proxy for {identity.IotHubHostName}/{identity.Id}");
            public static void SendingTwinUpdate(IIdentity identity) => Log.LogDebug((int)EventIds.SendingTwinUpdate, $"Sending twin update to {identity.Id}");
        }
    }
}
