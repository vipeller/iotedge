// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Hub.MqttBrokerAdapter.Test
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Edge.Hub.MqttBrokerAdapter;
    using Microsoft.Azure.Devices.Edge.Util.Test.Common;
    using Xunit;

    [Integration]
    public class MqttBridgeConnectorTest
    {
        const int Port = 8000;
        const string host = "127.0.0.1";

        [Fact]
        public async Task StartsUpAndServes()
        {
            var cts = new CancellationTokenSource();
            var broker = new LeanMqtt(Port);
            var brokerTask = broker.Start(cts.Token);

            var timeout = TimeSpan.FromSeconds(10);

            var discovery = new DiscoveryStub().WithSubscibers("devices/+");

            var suv = new MqttBridgeConnector(discovery);

            await suv.ConnectAsync(host, Port);

            var result = await broker.StartScenario()
                                     .Then(s => s.CheckClientConnected(timeout))
                                     .Then(s => s.CheckClientSubscribed("devices/+", timeout))
                                     .Execute();

            await suv.DisconnectAsync();

            cts.Cancel();
            await brokerTask;

            Assert.True(result);
        }

        [Fact]
        public async Task SubscribesToTopics()
        {
            var cts = new CancellationTokenSource();
            var broker = new LeanMqtt(Port);
            var brokerTask = broker.Start(cts.Token);

            var timeout = TimeSpan.FromSeconds(10);

            var discovery = new DiscoveryStub()
                                    .WithSubscibers("alfa", "beta")
                                    .WithSubscibers("gamma")
                                    .WithSubscibers("delta");

            var suv = new MqttBridgeConnector(discovery);

            await suv.ConnectAsync(host, Port);

            var result = await broker.StartScenario()
                                     .Then(s => s.CheckClientSubscribed("alfa", timeout))
                                     .Then(s => s.CheckClientSubscribed("beta", timeout))
                                     .Then(s => s.CheckClientSubscribed("gamma", timeout))
                                     .Then(s => s.CheckClientSubscribed("delta", timeout))
                                     .Execute();

            await suv.DisconnectAsync();

            cts.Cancel();
            await brokerTask;

            Assert.True(result);
        }

        [Fact]
        public async Task Reconnects()
        {
            var cts = new CancellationTokenSource();
            var broker = new LeanMqtt(Port);
            var brokerTask = broker.Start(cts.Token);

            var timeout = TimeSpan.FromSeconds(10);
            
            var suv = new MqttBridgeConnector(new DiscoveryStub());

            await suv.ConnectAsync(host, Port);

            var result = await broker.StartScenario()
                                     .Then(s => s.CheckClientConnected(timeout))                                     
                                     .Execute();

            Assert.True(result);

            cts.Cancel();
            await brokerTask;

            cts = new CancellationTokenSource();
            broker = new LeanMqtt(Port);
            brokerTask = broker.Start(cts.Token);

            result = await broker.StartScenario()
                                     .Then(s => s.CheckClientConnected(timeout))
                                     .Execute();

            await suv.DisconnectAsync();

            cts.Cancel();
            await brokerTask;

            Assert.True(result);
        }

        [Fact]
        public async Task ResubscribesAfterReconnect()
        {
            var discovery = new DiscoveryStub()
                                    .WithSubscibers("alfa", "beta")
                                    .WithSubscibers("gamma")
                                    .WithSubscibers("delta");

            var cts = new CancellationTokenSource();
            var broker = new LeanMqtt(Port);
            var brokerTask = broker.Start(cts.Token);

            var timeout = TimeSpan.FromSeconds(10);

            var suv = new MqttBridgeConnector(discovery);

            await suv.ConnectAsync(host, Port);

            var result = await broker.StartScenario()
                                     .Then(s => s.CheckClientConnected(timeout))
                                     .Execute();

            Assert.True(result);

            cts.Cancel();
            await brokerTask;

            var oldBroker = broker;

            cts = new CancellationTokenSource();
            broker = new LeanMqtt(Port);
            brokerTask = broker.Start(cts.Token);

            result = await broker.StartScenario()
                                 .Then(s => s.CheckClientConnected(timeout))
                                 .Execute();

            Assert.True(result);

            await Task.Delay(2000);

            result = await broker.StartScenario()
                                 .Then(s => s.CheckClientSubscribed("alfa", timeout))
                                 .Then(s => s.CheckClientSubscribed("beta", timeout))
                                 .Then(s => s.CheckClientSubscribed("gamma", timeout))
                                 .Then(s => s.CheckClientSubscribed("delta", timeout))
                                 .Execute();

            await suv.DisconnectAsync();

            cts.Cancel();
            await brokerTask;

            Assert.True(result);
        }

        [Fact]
        public async Task ForwardsBrokerMessagesToConsumers()
        {
            var cts = new CancellationTokenSource();
            var broker = new LeanMqtt(Port);
            var brokerTask = broker.Start(cts.Token);

            var timeout = TimeSpan.FromSeconds(10);

            var consumer1 = new ConcurrentQueue<MqttPublishInfo>();
            var consumer2 = new ConcurrentQueue<MqttPublishInfo>();

            var milestone = new SemaphoreSlim(0, 2);

            var discovery = new DiscoveryStub()
                                    .WithConsumer(m => { consumer1.Enqueue(m); milestone.Release(); return Task.FromResult(true); })
                                    .WithConsumer(m => { consumer2.Enqueue(m); milestone.Release(); return Task.FromResult(true); });

            var suv = new MqttBridgeConnector(discovery);

            await suv.ConnectAsync(host, Port);

            await broker.StartScenario()
                        .Then(s => s.PublishMessage("alfa", Encoding.ASCII.GetBytes("message")))
                        .Execute();

            await milestone.WaitAsync();

            await suv.DisconnectAsync();

            cts.Cancel();
            await brokerTask;

            consumer1.TryDequeue(out var consumer1Result);
            consumer2.TryDequeue(out var consumer2Result);

            Assert.Equal("alfa", consumer1Result.Topic);
            Assert.Equal("alfa", consumer2Result.Topic);
            Assert.Equal("message", Encoding.ASCII.GetString(consumer1Result.Payload));
            Assert.Equal("message", Encoding.ASCII.GetString(consumer1Result.Payload));
        }

        [Fact]
        public async Task ConsumerThrowsRestGetMessages()
        {
            var cts = new CancellationTokenSource();
            var broker = new LeanMqtt(Port);
            var brokerTask = broker.Start(cts.Token);

            var timeout = TimeSpan.FromSeconds(10);

            var consumer = new ConcurrentQueue<MqttPublishInfo>();

            var milestone = new SemaphoreSlim(0, 2);

            var discovery = new DiscoveryStub()
                                    .WithConsumer(m => { milestone.Release(); throw new Exception(); })
                                    .WithConsumer(m => { consumer.Enqueue(m); milestone.Release(); return Task.FromResult(true); });

            var suv = new MqttBridgeConnector(discovery);

            await suv.ConnectAsync(host, Port);

            await broker.StartScenario()
                        .Then(s => s.PublishMessage("alfa", Encoding.ASCII.GetBytes("message")))
                        .Execute();

            await milestone.WaitAsync();

            await suv.DisconnectAsync();

            cts.Cancel();
            await brokerTask;

            consumer.TryDequeue(out var consumerResult);

            Assert.Equal("alfa", consumerResult.Topic);
            Assert.Equal("message", Encoding.ASCII.GetString(consumerResult.Payload));
        }

        [Fact]
        public async Task ForwardsProducerMessagesToBroker()
        {
            var cts = new CancellationTokenSource();
            var broker = new LeanMqtt(Port);
            var brokerTask = broker.Start(cts.Token);

            var timeout = TimeSpan.FromSeconds(10);

            var producer = new ProducerStub();
            var discovery = new DiscoveryStub()
                                    .WithProducer(producer);

            var suv = new MqttBridgeConnector(discovery);

            await suv.ConnectAsync(host, Port);

            await producer.SendMessage("alfa", Encoding.ASCII.GetBytes("message"));

            var result = await broker.StartScenario()
                                     .Then(s => s.CheckClientPublished("alfa", Encoding.ASCII.GetBytes("message"), timeout))
                                     .Execute();

            await suv.DisconnectAsync();

            cts.Cancel();
            await brokerTask;

            Assert.True(result);
        }
    }

    public class DiscoveryStub : IComponentDiscovery
    {
        public DiscoveryStub()
        {
            this.Subscribers = new IMqttSubscriber[0];
            this.Producers = new IMqttMessageProducer[0];
            this.Consumers = new IMqttMessageConsumer[0];
        }

        public IReadOnlyCollection<IMqttSubscriber> Subscribers { get; set; }
        public IReadOnlyCollection<IMqttMessageProducer> Producers { get; set; }    
        public IReadOnlyCollection<IMqttMessageConsumer> Consumers { get; set; }

        public DiscoveryStub WithSubscibers(params string[] topics)
        {
            this.Subscribers = this.Subscribers.Concat(new[] { new SubscriberStub() { Subscriptions = topics } }).ToArray();
            return this;
        }

        public DiscoveryStub WithConsumer(Func<MqttPublishInfo, Task<bool>> consumer)
        {
            this.Consumers = this.Consumers.Concat(new[] { new ConsumerStub(consumer) }).ToArray();
            return this;
        }

        public DiscoveryStub WithProducer(IMqttMessageProducer producer)
        {
            this.Producers = this.Producers.Concat(new[] { producer }).ToArray();
            return this;
        }
    }

    public class ConsumerStub : IMqttMessageConsumer
    {
        readonly Func<MqttPublishInfo, Task<bool>> handler;

        public ConsumerStub(Func<MqttPublishInfo, Task<bool>> handler) =>  this.handler = handler;        
        public Task<bool> HandleAsync(MqttPublishInfo publishInfo) => this.handler(publishInfo);
    }

    public class SubscriberStub : IMqttSubscriber
    {
        public IReadOnlyCollection<string> Subscriptions { get; set; }
    }

    public class ProducerStub : IMqttMessageProducer
    {
        IMqttBridgeConnector connector;

        public void SetConnector(IMqttBridgeConnector connector) => this.connector = connector;
        public Task<bool> SendMessage(string topic, byte[] payload) => this.connector.SendAsync(topic, payload);
    }
}
