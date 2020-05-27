// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Hub.MqttBrokerAdapter
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading.Tasks;

    using Microsoft.Azure.Devices.Edge.Hub.Core;
    using Microsoft.Azure.Devices.Edge.Hub.Core.Device;
    using Microsoft.Azure.Devices.Edge.Hub.Core.Identity;
    using Microsoft.Azure.Devices.Edge.Util;
    using Microsoft.Azure.Devices.Edge.Util.Concurrency;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;

    public class ConnectionHandler : IConnectionRegistry, ISubscriber, IMessageConsumer
    {
        const string TopicDeviceConnected = "$edgehub/connected";

        static readonly char[] identitySegmentSeparator = new[] { '/' };
        static readonly string[] subscriptions = new[] { TopicDeviceConnected };

        readonly IConnectionProvider connectionProvider;

        AsyncLock guard = new AsyncLock();

        // FIXME change it back to Dictionary once migrated to newer framework. Using it only for TryAdd()
        ConcurrentDictionary<IIdentity, IDeviceListener> knownConnections = new ConcurrentDictionary<IIdentity, IDeviceListener>();

        // this class is auto-registered so no way to implement an async activator.
        // hence this one needs to get a Task<T> which is suboptimal, but that is the way
        // IConnectionProvider is registered
        public ConnectionHandler(Task<IConnectionProvider> connectionProvider)
        {
            this.connectionProvider = connectionProvider.Result;
        }

        public IReadOnlyCollection<string> Subscriptions => subscriptions;

        public async Task<Option<IDeviceListener>> GetUpstreamProxyAsync(IIdentity identity)
        {
            using (await this.guard.LockAsync())
            {
                if (this.knownConnections.TryGetValue(identity, out var result))
                {
                    return Option.Some(result);
                }
                else
                {
                    return Option.None<IDeviceListener>();
                }
            }
        }

        public async Task<Option<IDeviceProxy>> GetDownstreamProxyAsync(IIdentity identity)
        {
            using (await this.guard.LockAsync())
            {
                if (this.knownConnections.TryGetValue(identity, out var container))
                {
                    if (container is IDeviceProxy result)
                    { 
                        return Option.Some(result);
                    }
                }

                return Option.None<IDeviceProxy>();
            }
        }

        public Task<bool> HandleAsync(MqttPublishInfo publishInfo)
        {
            switch (publishInfo.Topic)
            {
                case TopicDeviceConnected:
                    return this.HandleDeviceConnectedAsync(publishInfo);

                default:
                    return Task.FromResult(true);
            }
        }

        async Task<bool> HandleDeviceConnectedAsync(MqttPublishInfo mqttPublishInfo)
        {            
            var updatedIdentities = GetIdentitiesFromUpdateMessage(mqttPublishInfo);

            await updatedIdentities.ForEachAsync(async i => await ReconcileConnectionsAsync(new HashSet<IIdentity>(i)));

           // await deviceListener.SendGetTwinRequest("abcde");

            return true;
        }

        async Task ReconcileConnectionsAsync(HashSet<IIdentity> updatedIdentities)
        {
            var identitiesAdded = default(HashSet<IIdentity>);
            var identitiesRemoved = default(HashSet<IIdentity>);

            using (await this.guard.LockAsync())
            {
                var knownIdentities = this.knownConnections.Keys;

                identitiesAdded = new HashSet<IIdentity>(updatedIdentities);
                identitiesAdded.ExceptWith(knownIdentities);

                identitiesRemoved = new HashSet<IIdentity>(knownIdentities);
                identitiesRemoved.ExceptWith(updatedIdentities);

                await RemoveConnectionsAsync(identitiesRemoved);
                await AddConnectionsAsync(identitiesAdded);
            }
        }

        async Task RemoveConnectionsAsync(HashSet<IIdentity> identitiesRemoved)
        {
            foreach (var identity in identitiesRemoved)
            {
                if (this.knownConnections.TryRemove(identity, out var deviceListener))
                {
                    await deviceListener.CloseAsync();
                }
                else
                {
                    Events.UnknownClientDisconnected(identity.Id);
                }
            }
        }

        async Task AddConnectionsAsync(HashSet<IIdentity> identitiesAdded)
        {
            foreach (var identity in identitiesAdded)
            {
                var deviceListener = await this.connectionProvider.GetDeviceListenerAsync(identity);
                var deviceProxy = new DeviceProxy(identity);
                deviceListener.BindDeviceProxy(deviceProxy);

                var previousListener = default(IDeviceListener);

                this.knownConnections.AddOrUpdate(
                    identity,
                    deviceListener,
                    (k, v) =>
                    {
                        previousListener = v;
                        return deviceListener;
                    });

                if (previousListener != null)
                {
                    Events.ExistingClientAdded(previousListener.Identity.Id);
                    await previousListener.CloseAsync();
                }
            }
        }

        Option<List<Identity>> GetIdentitiesFromUpdateMessage(MqttPublishInfo mqttPublishInfo)
        {
            //return Option.Some(new List<Identity> { new ModuleIdentity("vikauthtest.azure-devices.net", "maki", "SimulatedTemperatureSensor") });

            var identityList = default(List<string>);

            try
            {
                var payloadAsString = Encoding.UTF8.GetString(mqttPublishInfo.Payload);
                identityList = JsonConvert.DeserializeObject<List<string>>(payloadAsString);
            }
            catch (Exception e)
            {
                Events.BadPayloadFormat(e);
                return Option.None<List<Identity>>();
            }

            var result = new List<Identity>();

            foreach (var id in identityList)
            {
                var identityComponents = id.Split(identitySegmentSeparator, StringSplitOptions.RemoveEmptyEntries);

                switch (identityComponents.Length)
                {
                    case 2:
                        result.Add(new DeviceIdentity(identityComponents[0], identityComponents[1]));
                        break;

                    case 3:
                        result.Add(new ModuleIdentity(identityComponents[0], identityComponents[1], identityComponents[2]));
                        break;

                    default:
                        Events.BadIdentityFormat(id);
                        continue;
                }              
            }

            return Option.Some(result);
        }

        static class Events
        {
            const int IdStart = MqttBridgeEventIds.ConnectionHandler;
            static readonly ILogger Log = Logger.Factory.CreateLogger<ConnectionHandler>();

            enum EventIds
            {
                BadPayloadFormat = IdStart,
                BadIdentityFormat,
                UnknownClientDisconnected,
                ExistingClientAdded
            }

            public static void BadPayloadFormat(Exception e) => Log.LogError((int)EventIds.BadPayloadFormat, e, "Bad payload format: cannot deserialize connection update");
            public static void BadIdentityFormat(string identity) => Log.LogError((int)EventIds.BadIdentityFormat, $"Bad identity format: {identity}");
            public static void UnknownClientDisconnected(string identity) => Log.LogWarning((int)EventIds.UnknownClientDisconnected, $"Received disconnect notification about a not-connected client {identity}");
            public static void ExistingClientAdded(string identity) => Log.LogWarning((int)EventIds.ExistingClientAdded, $"Received connect notification about a already-connected client {identity}");
        }
    }
}
