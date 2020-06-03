// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Hub.MqttBrokerAdapter
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Edge.Hub.Core;
    using Microsoft.Azure.Devices.Edge.Hub.Core.Identity;
    using Microsoft.Azure.Devices.Edge.Util;
    using Microsoft.Extensions.Logging;

    public class TwinHandler : ITwinHandler, ISubscriber, IMessageConsumer, IMessageProducer
    {
        const string TopicTwinGet = "$iothub/twin/GET/";
        const string TopicTwinGetSubscription = TopicTwinGet + "#";
        const string TopicTwinGetRequest = TopicTwinGet + "?$rid=";

        const string TopicTwinResult = "$iothub/twin/res/{0}/?$rid={1}";

        static readonly string[] subscriptions = new[] { TopicTwinGetSubscription };

        IMqttBridgeConnector connector;

        public IReadOnlyCollection<string> Subscriptions => subscriptions;

        readonly IConnectionRegistry connectionRegistry;

        public TwinHandler(IConnectionRegistry connectionRegistry) => this.connectionRegistry = connectionRegistry;

        public async Task<bool> HandleAsync(MqttPublishInfo publishInfo)
        {
            if (!publishInfo.Topic.StartsWith(TopicTwinGetRequest))
            {
                return false;
            }
            
            // FIXME this should come from the transleted topic names:
            var identity = new ModuleIdentity("vikauthtest.azure-devices.net", "maki", "Custom");
            var proxy = await this.connectionRegistry.GetUpstreamProxyAsync(identity);
            var requestId = GetRequestIdFromTopic(publishInfo.Topic);

            try
            {
                _ = proxy.Expect(() => new Exception($"No upstream proxy found for {identity.Id}")).SendGetTwinRequest(requestId);
            }
            catch (Exception)
            {
                Events.MissingProxy(identity.Id);
                return false;
            }

            return true;
        }

        public void SetConnector(IMqttBridgeConnector connector) => this.connector = connector;

        public async Task SendTwinUpdate(IMessage twin, IIdentity identity)
        {           
            var statusCode = string.Empty;
            var correlationId = string.Empty;

            var allPropertiesPresent = true;

            allPropertiesPresent = allPropertiesPresent && twin.SystemProperties.TryGetValue(SystemProperties.StatusCode, out statusCode);
            allPropertiesPresent = allPropertiesPresent && twin.SystemProperties.TryGetValue(SystemProperties.CorrelationId, out correlationId); 

            if (allPropertiesPresent)
            {
                var result = await this.connector.SendAsync(
                                        GetTopicFromMessageProperties(statusCode, correlationId),
                                        twin.Body);

                if (result)
                {
                    Events.TwinUpdate(identity.Id, statusCode, correlationId, twin.Body.Length);
                }
                else
                {
                    Events.TwinUpdateFailed(identity.Id, statusCode, correlationId, twin.Body.Length);
                }
            }
            else
            {
                Events.TwinUpdateIncompete(identity.Id);
            }

            //   var propertyBag = UrlEncodedDictionarySerializer.Serialize(twin.Properties.Concat(twin.SystemProperties));

            return;
        }

        string GetRequestIdFromTopic(string topic) => topic.Substring(TopicTwinGetRequest.Length);
        string GetTopicFromMessageProperties(string statusCode, string correlationId) => string.Format(TopicTwinResult, statusCode, correlationId);

        static class Events
        {
            const int IdStart = MqttBridgeEventIds.TwinHandler;
            static readonly ILogger Log = Logger.Factory.CreateLogger<TwinHandler>();

            enum EventIds
            {
                TwinUpdate = IdStart,
                TwinUpdateFailed,
                TwinUpdateIncompete,
                MissingProxy
            }

            public static void TwinUpdate(string id, string statusCode, string correlationId, int messageLen) => Log.LogDebug((int)EventIds.TwinUpdate, $"Twin Update sent to device: [{id}], status: [{statusCode}], rid: [{correlationId}], msg len: [{messageLen}]");
            public static void TwinUpdateFailed(string id, string statusCode, string correlationId, int messageLen) => Log.LogError((int)EventIds.TwinUpdateFailed, $"Failed to send Twin Update to device: [{id}], status: [{statusCode}], rid: [{correlationId}], msg len: [{messageLen}]");
            public static void TwinUpdateIncompete(string id) => Log.LogError((int)EventIds.TwinUpdateIncompete, $"Failed to send Twin Update to device [{id}] because the message is incomplete - not all system properties are present");
            public static void MissingProxy(string id) => Log.LogError((int)EventIds.MissingProxy, $"Missing proxy for [{id}]");
        }
    }
}
