// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Hub.MqttBrokerAdapter.Test
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Edge.Hub.Core;
    using Microsoft.Azure.Devices.Edge.Hub.Core.Device;
    using Microsoft.Azure.Devices.Edge.Hub.Core.Identity;
    using Microsoft.Azure.Devices.Edge.Util;
    using Microsoft.Azure.Devices.Edge.Util.Test.Common;
    using Moq;
    using Xunit;

    [Unit]
    public class SubscriptionChangeHandlerTest
    {
        [Theory]
        [MemberData(nameof(NonSubscriptionTopics))]
        public async Task DoesNotHandleNonSubscriptionTopics(string topic)
        {
            var publishInfo = new MqttPublishInfo(topic, new byte[0]);
            var (cloud2DeviceMessageHandler, moduleToModuleMessageHandler, directMethodHandler) = GetSubscriptionWatchers();
            var (connectionRegistry, identityProvider) = GetHandlerDependencies();

            var sut = new SubscriptionChangeHandler(
                            cloud2DeviceMessageHandler,
                            moduleToModuleMessageHandler,
                            directMethodHandler,
                            connectionRegistry,
                            identityProvider);

            var isHandled = await sut.HandleAsync(publishInfo);

            Assert.False(isHandled);
        }

        [Theory]
        [MemberData(nameof(SubscriptionTopics))]
        public async Task HandlesSubscriptionTopics(string topic)
        {
            var publishInfo = new MqttPublishInfo(topic, new byte[0]);
            var (cloud2DeviceMessageHandler, moduleToModuleMessageHandler, directMethodHandler) = GetSubscriptionWatchers();
            var (connectionRegistry, identityProvider) = GetHandlerDependencies();

            var sut = new SubscriptionChangeHandler(
                            cloud2DeviceMessageHandler,
                            moduleToModuleMessageHandler,
                            directMethodHandler,
                            connectionRegistry,
                            identityProvider);

            var isHandled = await sut.HandleAsync(publishInfo);

            Assert.True(isHandled);
        }
      
        [Fact]
        public async Task CapturesDeviceIdentityFromTopic()
        {
            var publishInfo = new MqttPublishInfo("$edgehub/captured_device_id/subscriptions", Encoding.UTF8.GetBytes("[]"));
            var (cloud2DeviceMessageHandler, moduleToModuleMessageHandler, directMethodHandler) = GetSubscriptionWatchers();
            var (connectionRegistry, _) = GetHandlerDependencies();
            var identityProvider = Mock.Of<IIdentityProvider>();

            string passedDeviceId = null;

            Mock.Get(identityProvider)
                .Setup(ip => ip.Create(It.IsAny<string>()))
                .Returns((string device_id) =>
                {
                    passedDeviceId = device_id;
                    return new DeviceIdentity("host", device_id);
                });

            var sut = new SubscriptionChangeHandler(
                            cloud2DeviceMessageHandler,
                            moduleToModuleMessageHandler,
                            directMethodHandler,
                            connectionRegistry,
                            identityProvider);

            _ = await sut.HandleAsync(publishInfo);

            Assert.Equal("captured_device_id", passedDeviceId);
        }

        [Fact]
        public async Task CapturesModuleIdentityFromTopic()
        {
            var publishInfo = new MqttPublishInfo("$edgehub/captured_device_id/captured_module_id/subscriptions", Encoding.UTF8.GetBytes("[]"));
            var (cloud2DeviceMessageHandler, moduleToModuleMessageHandler, directMethodHandler) = GetSubscriptionWatchers();
            var (connectionRegistry,  _) = GetHandlerDependencies();
            var identityProvider = Mock.Of<IIdentityProvider>();

            string passedDeviceId = null;
            string passedModuleId = null;

            Mock.Get(identityProvider)
                .Setup(ip => ip.Create(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string device_id, string module_id) =>
                {
                    passedDeviceId = device_id;
                    passedModuleId = module_id;
                    return new DeviceIdentity("host", device_id);
                });

            var sut = new SubscriptionChangeHandler(
                            cloud2DeviceMessageHandler,
                            moduleToModuleMessageHandler,
                            directMethodHandler,
                            connectionRegistry,
                            identityProvider);

            _ = await sut.HandleAsync(publishInfo);

            Assert.Equal("captured_device_id", passedDeviceId);
            Assert.Equal("captured_module_id", passedModuleId);
        }

        [Fact]
        public async Task TurnsOnOffSubscriptions()
        {            
            var listenerCapture = new DeviceListenerCapture();

            var publishInfo = new MqttPublishInfo("$edgehub/device_id/module_id/subscriptions", Encoding.UTF8.GetBytes("[\"$edgehub/device_id/module_id/MatchingPattern\"]"));
            var (_, moduleToModuleMessageHandler, directMethodHandler) = GetSubscriptionWatchers();

            var cloud2DeviceMessageHandler = Mock.Of<ICloud2DeviceMessageHandler>();
            Mock.Get(cloud2DeviceMessageHandler)
                .Setup(sw => sw.WatchedSubscriptions)
                .Returns(() => new List<SubscriptionPattern>()
                    {
                        new SubscriptionPattern(@"(?<id1>[^/\+\#]+)(/(?<id2>[^/\+\#]+))?/patternX", DeviceSubscription.Methods),
                        new SubscriptionPattern(@"(?<id1>[^/\+\#]+)(/(?<id2>[^/\+\#]+))?/MatchingPattern", DeviceSubscription.C2D),
                    });

            var (connectionRegistry, identityProvider) = GetHandlerDependencies(listenerCapture: listenerCapture);

            var sut = new SubscriptionChangeHandler(
                            cloud2DeviceMessageHandler,
                            moduleToModuleMessageHandler,
                            directMethodHandler,
                            connectionRegistry,
                            identityProvider);

            _ = await sut.HandleAsync(publishInfo);

            Assert.Equal(DeviceSubscription.C2D, listenerCapture.Captured.AddedSubscription);
            Assert.Equal(DeviceSubscription.Methods, listenerCapture.Captured.RemovedSubscription);
        }

        [Fact]
        public async Task DifferentIdentityDoesNotMatch()
        {
            var listenerCapture = new DeviceListenerCapture();

            var publishInfo = new MqttPublishInfo("$edgehub/device_id/module_id/subscriptions", Encoding.UTF8.GetBytes("[\"$edgehub/device_id_x/module_id_x/MatchingPattern\"]"));
            var (_, moduleToModuleMessageHandler, directMethodHandler) = GetSubscriptionWatchers();

            var cloud2DeviceMessageHandler = Mock.Of<ICloud2DeviceMessageHandler>();
            Mock.Get(cloud2DeviceMessageHandler)
                .Setup(sw => sw.WatchedSubscriptions)
                .Returns(() => new List<SubscriptionPattern>()
                    {
                        new SubscriptionPattern(@"(?<id1>[^/\+\#]+)(/(?<id2>[^/\+\#]+))?/MatchingPattern", DeviceSubscription.C2D)
                    });

            var (connectionRegistry, identityProvider) = GetHandlerDependencies(listenerCapture: listenerCapture);
            var sut = new SubscriptionChangeHandler(
                             cloud2DeviceMessageHandler,
                             moduleToModuleMessageHandler,
                             directMethodHandler,
                             connectionRegistry,
                             identityProvider);

            _ = await sut.HandleAsync(publishInfo);

            Assert.Equal(DeviceSubscription.C2D, listenerCapture.Captured.RemovedSubscription);            
        }

        public static IEnumerable<object[]> NonSubscriptionTopics()
        {
            var testStrings = new[] { "$edgehub/device_id",
                                      "$edgehub/device_id/something/module_id/subscriptions",
                                      "$edgehub/subscriptions",
                                      "$edgehub/device_id/module_id/subscriptions/something",
                                      "something/$edgehub/device_id/module_id/subscriptions"                                      
            };

            return testStrings.Select(s => new string[] { s });
        }

        public static IEnumerable<object[]> SubscriptionTopics()
        {
            var testStrings = new[] { "$edgehub/device_id/module_id/subscriptions",
                                      "$edgehub/device_id/subscriptions"
            };

            return testStrings.Select(s => new string[] { s });
        }

        (ICloud2DeviceMessageHandler, IModuleToModuleMessageHandler, IDirectMethodHandler) GetSubscriptionWatchers()
        {
            var cloud2DeviceMessageHandler = Mock.Of<ICloud2DeviceMessageHandler>();
            var moduleToModuleMessageHandler = Mock.Of<IModuleToModuleMessageHandler>();
            var directMethodHandler = Mock.Of<IDirectMethodHandler>();

            Mock.Get(cloud2DeviceMessageHandler)
                .Setup(sw => sw.WatchedSubscriptions)
                .Returns(() => new List<SubscriptionPattern>());

            Mock.Get(moduleToModuleMessageHandler)
                .Setup(sw => sw.WatchedSubscriptions)
                .Returns(() => new List<SubscriptionPattern>());

            Mock.Get(directMethodHandler)
                .Setup(sw => sw.WatchedSubscriptions)
                .Returns(() => new List<SubscriptionPattern>());

            return (cloud2DeviceMessageHandler, moduleToModuleMessageHandler, directMethodHandler);
        }

        (IConnectionRegistry, IIdentityProvider) GetHandlerDependencies(bool shouldFindProxy = true, DeviceListenerCapture listenerCapture = null)
        {
            var connectionRegistry = Mock.Of<IConnectionRegistry>();
            var identityProvider = Mock.Of<IIdentityProvider>();

            Mock.Get(identityProvider)
                .Setup(ip => ip.Create(It.IsAny<string>()))
                .Returns((string id) => new DeviceIdentity("host", id));

            Mock.Get(identityProvider)
                .Setup(ip => ip.Create(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string device_id, string module_id) => new ModuleIdentity("host", device_id, module_id));

            Mock.Get(connectionRegistry)
                .Setup(cr => cr.GetDeviceListenerAsync(It.IsAny<IIdentity>()))
                .Returns((IIdentity i) => CreateListenerFromIdentity(i));

            return (connectionRegistry, identityProvider);

            Task<Option<IDeviceListener>> CreateListenerFromIdentity(IIdentity identity)
            {
                var listener = default(TestDeviceListener);

                if (shouldFindProxy)
                {
                    listener = new TestDeviceListener(identity);
                    if (listenerCapture != null)
                    {
                        listenerCapture.Capture(listener);
                    }
                }

                return Task.FromResult(listener != null
                                            ? Option.Some(listener as IDeviceListener)
                                            : Option.None<IDeviceListener>());
            }
        }

        class DeviceListenerCapture
        {
            public TestDeviceListener Captured { get; private set; }
            public void Capture(TestDeviceListener testListener) => this.Captured = testListener;
        }

        class TestDeviceListener : IDeviceListener
        {
            public TestDeviceListener(IIdentity identity)
            {
                this.Identity = identity;
                this.AddedSubscription = DeviceSubscription.Unknown;
                this.RemovedSubscription = DeviceSubscription.Unknown;
            }

            public DeviceSubscription AddedSubscription { get; private set; }
            public DeviceSubscription RemovedSubscription { get; private set; }

            public IIdentity Identity { get; }

            public Task AddDesiredPropertyUpdatesSubscription(string correlationId) => Task.CompletedTask;
            
            public Task CloseAsync() => Task.CompletedTask;
            public Task ProcessDeviceMessageBatchAsync(IEnumerable<IMessage> message) => Task.CompletedTask;
            public Task ProcessMessageFeedbackAsync(string messageId, FeedbackStatus feedbackStatus) => Task.CompletedTask;
            public Task ProcessMethodResponseAsync(IMessage message) => Task.CompletedTask;
            public Task RemoveDesiredPropertyUpdatesSubscription(string correlationId) => Task.CompletedTask;
            
            public Task SendGetTwinRequest(string correlationId) => Task.CompletedTask;
            public Task UpdateReportedPropertiesAsync(IMessage reportedPropertiesMessage, string correlationId) => Task.CompletedTask;
            public Task ProcessDeviceMessageAsync(IMessage message) => Task.CompletedTask;

            public void BindDeviceProxy(IDeviceProxy deviceProxy)
            {
            }

            public Task AddSubscription(DeviceSubscription subscription)
            {
                this.AddedSubscription = subscription;
                return Task.CompletedTask;
            }

            public Task RemoveSubscription(DeviceSubscription subscription)
             {
                this.RemovedSubscription = subscription;
                return Task.CompletedTask;
            }
        }
    }
}
