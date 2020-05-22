// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Hub.MqttBrokerAdapter
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using Microsoft.Azure.Devices.Edge.Hub.Core;
    using Microsoft.Azure.Devices.Edge.Hub.Core.Identity;

    public class ConnectionHandler : ISubscriber, IMessageConsumer
    {
        const string TopicDeviceConnected = "edgehub/connected";

        static readonly string[] subscriptions = new[] { TopicDeviceConnected };

        readonly IConnectionProvider connectionProvider;

        // this class is auto-registered so no way to implement an async activator.
        // hence this one needs to get a Task<T> which is suboptimal, but that is the way
        // IConnectionProvider is registered
        public ConnectionHandler(Task<IConnectionProvider> connectionProvider)
        {
            this.connectionProvider = connectionProvider.Result;
        }

        public IReadOnlyCollection<string> Subscriptions => subscriptions;

        public Task<bool> HandleAsync(MqttPublishInfo publishInfo)
        {
            switch (publishInfo.Topic)
            {
                case TopicDeviceConnected:
                    return this.HandleDeviceConnected(publishInfo);

                default:
                    return Task.FromResult(true);
            }
        }

        async Task<bool> HandleDeviceConnected(MqttPublishInfo mqttPublishInfo)
        {
            var identity = new ModuleIdentity("vikauthtest.azure-devices.net", "maki", "SimulatedTemperatureSensor");

            var deviceListener = await this.connectionProvider.GetDeviceListenerAsync(identity);
            var deviceProxy = new DeviceProxy();
            deviceListener.BindDeviceProxy(deviceProxy);

            await deviceListener.SendGetTwinRequest("abcde");

            return true;
        }
    }
}
