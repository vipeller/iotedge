namespace Microsoft.Azure.Devices.Edge.Hub.MqttBrokerAdapter
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.Azure.Devices.Edge.Hub.Core;
    using Microsoft.Azure.Devices.Edge.Hub.Core.Identity;

    public class PocCloudProxyDispatcher : IMessageConsumer, IMessageProducer
    {
        int nextRid = 1;
        IEdgeHub edgeHub;

        Dictionary<string, TaskCompletionSource<IMessage>> pendingTwinRequests = new Dictionary<string, TaskCompletionSource<IMessage>>();

        TaskCompletionSource<IMqttBrokerConnector> connector = new TaskCompletionSource<IMqttBrokerConnector>();

        public IReadOnlyCollection<string> Subscriptions => new string[] { "$downstream/#" };

        public void BindEdgeHub(IEdgeHub edgeHub)
        {
            this.edgeHub = edgeHub;
        }

        // FIXME: this should be able to switch state:
        public bool IsActive => true;

        public void SetConnector(IMqttBrokerConnector connector) => this.connector.SetResult(connector);

        public Task<bool> CloseAsync(IIdentity identity)
        {
            throw new NotImplementedException();
        }

        public async Task<IMessage> GetTwinAsync(IIdentity identity)
        {
            var rid = Interlocked.Increment(ref nextRid);

            var connector = await GetConnector();
            await connector.SendAsync($"$upstream/{identity.Id}/twin/get/?$rid={rid}", new byte[0]);

            var tsc = new TaskCompletionSource<IMessage>();
            this.pendingTwinRequests[rid.ToString()] = tsc;

            return await tsc.Task;
        }

        public Task<bool> HandleAsync(MqttPublishInfo publishInfo)
        {
            const string TwinGetResponsePattern = @"^\$downstream/(?<id1>[^/\+\#]+)(/(?<id2>[^/\+\#]+))?/twin/res/(?<res>.+)/\?\$rid=(?<rid>.+)";
            const string TwinUpdatePattern = @"^\$downstream/(?<id1>[^/\+\#]+)(/(?<id2>[^/\+\#]+))?/twin/desired/\?\$version=(?<version>.+)";
            const string MethodCallPattern = @"^\$downstream/(?<id1>[^/\+\#]+)/methods/(?<name>[^/\+\#]+)/\?\$rid=(?<rid>.+)";
            const string C2DPattern = @"^\$downstream/(?<id1>[^/\+\#]+)/devicebound/(?<lock>[^/\+\#]+)";

            var match = Regex.Match(publishInfo.Topic, TwinGetResponsePattern);
            if (match.Success)
            {
                var message = new EdgeMessage.Builder(publishInfo.Payload);
                message.SetSystemProperties(
                    new Dictionary<string, string>()
                    {
                        [SystemProperties.StatusCode] = match.Groups["res"].Value,
                        [SystemProperties.CorrelationId] = match.Groups["rid"].Value,
                    });

                this.pendingTwinRequests[match.Groups["rid"].Value].SetResult(message.Build());
                this.pendingTwinRequests.Remove(match.Groups["rid"].Value);

                return Task.FromResult(true);
            }

            match = Regex.Match(publishInfo.Topic, TwinUpdatePattern);
            if (match.Success)
            {
                var id = match.Groups["id2"].Success ? string.Format("{0}/{1}", match.Groups["id1"].Value, match.Groups["id2"].Value) : match.Groups["id1"].Value;
                var version = match.Groups["version"].Value;

                // FIXME check what should happen if the task comes with an error (if that is a case)
                _ = this.edgeHub.UpdateDesiredPropertiesAsync(
                                    id,
                                    new EdgeMessage.Builder(publishInfo.Payload)
                                                   .SetSystemProperties(new Dictionary<string, string>() { [SystemProperties.Version] = version}).Build());

                return Task.FromResult(true);
            }

            match = Regex.Match(publishInfo.Topic, MethodCallPattern);
            if (match.Success)
            {
                var id = match.Groups["id2"].Success ? string.Format("{0}/{1}", match.Groups["id1"].Value, match.Groups["id2"].Value) : match.Groups["id1"].Value;
                var method = match.Groups["name"].Value;
                var rid = match.Groups["rid"].Value;

                // FIXME check what should happen if the task comes with an error (if that is a case)
                var callingTask = this.edgeHub.InvokeMethodAsync(rid, new DirectMethodRequest(id, method, publishInfo.Payload, TimeSpan.FromMinutes(1)));
                callingTask.ContinueWith(
                    async response =>
                    {
                        var connector = await GetConnector();
                        await connector.SendAsync($"$upstream/{id}/methods/res/{response.Result.Status}/?$rid={rid}", response.Result.Data);
                    });

                return Task.FromResult(true);
            }

            match = Regex.Match(publishInfo.Topic, C2DPattern);
            if (match.Success)
            {
                var lockToken = match.Groups["lock"].Value;
                this.edgeHub.SendC2DMessageAsync(
                            match.Groups["id1"].Value,
                            new EdgeMessage.Builder(publishInfo.Payload)
                                            .SetSystemProperties(
                                                    new Dictionary<string, string>()
                                                    {
                                                        [SystemProperties.LockToken] = lockToken,
                                                        [SystemProperties.MsgCorrelationId] = lockToken,
                                                        [SystemProperties.MessageId] = lockToken,
                                                        [SystemProperties.CorrelationId] = lockToken,
                                                    }).Build());
            }

            return Task.FromResult(false);
        }

        public Task<bool> OpenAsync(IIdentity identity)
        {
            Console.WriteLine("!!!!! OpenAsync() called");
            return Task.FromResult(true);
        }

        public Task RemoveCallMethodAsync(IIdentity identity)
        {
            Console.WriteLine("!!!!! RemoveCallMethodAsync() called");
            return Task.CompletedTask;
        }

        public Task RemoveDesiredPropertyUpdatesAsync(IIdentity identity)
        {
            Console.WriteLine("!!!!! RemoveDesiredPropertyUpdatesAsync() called");
            return Task.CompletedTask;
        }

        public Task SendFeedbackMessageAsync(IIdentity identity, string messageId, FeedbackStatus feedbackStatus)
        {
            Console.WriteLine("!!!!! SendFeedbackMessageAsync() called");
            return Task.CompletedTask;
        }

        public async Task SendMessageAsync(IIdentity identity, IMessage message)
        {
            // fixme: should encode the properties:
            var connector = await GetConnector();
            await connector.SendAsync($"$upstream/{identity.Id}/messages/events", message.Body); // FIXME should encode a confirmation id
        }

        public async Task SendMessageBatchAsync(IIdentity identity, IEnumerable<IMessage> inputMessages)
        {
            // fixme: should encode the properties:
            var connector = await GetConnector();

            // fixme is there a better way?
            foreach (var message in inputMessages)
            {
                await connector.SendAsync($"$upstream/{identity.Id}/messages/events", message.Body);
            }
        }

        public async Task SetupCallMethodAsync(IIdentity identity)
        {
            var connector = await GetConnector();

            // this is $upstream instead of $edgehub, because when a twin message is to be sent to the client
            // that also goes on $edgehub/id/twin/res - so with this subscription we would get back the same message
            await connector.SubscribeAsync($"$upstream/{identity.Id}/methods/#");

            return;
        }

        public async Task SetupDesiredPropertyUpdatesAsync(IIdentity identity)
        {
            var connector = await GetConnector();

            // this is $upstream instead of $edgehub, because when a twin message is to be sent to the client
            // that also goes on $edgehub/id/twin/res - so with this subscription we would get back the same message
            await connector.SubscribeAsync($"$upstream/{identity.Id}/twin/res/#");

            return;
        }

        public Task StartListening(IIdentity identity)
        {
            // FIXME this should tell the bridge to start listen, but the simulator listens anyway
            Console.WriteLine("########## START LISTEN");
            return Task.CompletedTask;
        }

        public async Task UpdateReportedPropertiesAsync(IIdentity identity, IMessage reportedPropertiesMessage)
        {
            var rid = Interlocked.Increment(ref nextRid);

            var connector = await GetConnector();
            await connector.SendAsync($"$upstream/{identity.Id}/twin/reported/?$rid={rid}", reportedPropertiesMessage.Body);

            var tsc = new TaskCompletionSource<IMessage>();
            this.pendingTwinRequests[rid.ToString()] = tsc;

            await tsc.Task;
        }

        async Task<IMqttBrokerConnector> GetConnector()
        {         
            var connector = await this.connector.Task;
            await connector.BetterOnConnected.Task;

            return connector;
        }
    }
}
