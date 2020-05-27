// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Hub.MqttBrokerAdapter
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    public class TwinHandler : ISubscriber, IMessageConsumer, IMessageProducer
    {
        public IReadOnlyCollection<string> Subscriptions => throw new System.NotImplementedException(); 
        public Task<bool> HandleAsync(MqttPublishInfo publishInfo)
        {
            throw new System.NotImplementedException();
        }

        public void SetConnector(IMqttBridgeConnector connector)
        {
            throw new System.NotImplementedException();
        }
    }
}
