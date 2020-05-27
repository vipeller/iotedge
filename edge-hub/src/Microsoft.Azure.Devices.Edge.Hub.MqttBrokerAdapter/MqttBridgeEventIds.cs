// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Hub.MqttBrokerAdapter
{
    public class MqttBridgeEventIds
    {
        const int EventIdStart = 7300;
        public const int MqttBridgeProtocolHead = EventIdStart;
        public const int MqttBridgeConnector = EventIdStart + 50;
        public const int ConnectionHandler = EventIdStart + 100;
        public const int DeviceProxy = EventIdStart + 150;
    }
}
