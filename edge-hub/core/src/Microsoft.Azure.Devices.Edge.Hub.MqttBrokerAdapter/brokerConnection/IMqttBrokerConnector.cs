// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Hub.MqttBrokerAdapter
{
    using System;
    using System.Threading.Tasks;

    public interface IMqttBrokerConnector
    {
        Task ConnectAsync(string serverAddress, int port);
        Task DisconnectAsync();

        Task<bool> SendAsync(string topic, byte[] payload);
        Task<bool> SubscribeAsync(string topic);
        Task<bool> UnsubscribeAsync(string topic);

        TaskCompletionSource<bool> BetterOnConnected { get; }
        event EventHandler OnConnected;
    }
}
