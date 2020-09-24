namespace Microsoft.Azure.Devices.Edge.Hub.MqttBrokerAdapter
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading.Tasks;

    using Microsoft.Azure.Devices.Edge.Hub.Core;
    using Microsoft.Azure.Devices.Edge.Hub.Core.Cloud;
    using Microsoft.Azure.Devices.Edge.Hub.Core.Identity;
    using Microsoft.Azure.Devices.Edge.Hub.MqttBrokerAdapter;

    public class PocCloudProxy : ICloudProxy
    {
        PocCloudProxyDispatcher cloudProxyDispatcher;
        IIdentity identity;
        
        public PocCloudProxy(IIdentity identity, PocCloudProxyDispatcher cloudProxyDispatcher)
        {
            this.identity = identity;
            this.cloudProxyDispatcher = cloudProxyDispatcher;
        }

        public bool IsActive => this.cloudProxyDispatcher.IsActive;

        public Task<bool> CloseAsync() => this.cloudProxyDispatcher.CloseAsync(this.identity);
        public Task<IMessage> GetTwinAsync() => this.cloudProxyDispatcher.GetTwinAsync(identity);
        public Task<bool> OpenAsync() => this.cloudProxyDispatcher.OpenAsync(identity);
        public Task RemoveCallMethodAsync() => this.cloudProxyDispatcher.RemoveCallMethodAsync(identity);
        public Task RemoveDesiredPropertyUpdatesAsync() => this.cloudProxyDispatcher.RemoveDesiredPropertyUpdatesAsync(identity);
        public Task SendFeedbackMessageAsync(string messageId, FeedbackStatus feedbackStatus) => this.cloudProxyDispatcher.SendFeedbackMessageAsync(this.identity, messageId, feedbackStatus);
        public Task SendMessageAsync(IMessage message) => this.cloudProxyDispatcher.SendMessageAsync(this.identity, message);
        public Task SendMessageBatchAsync(IEnumerable<IMessage> inputMessages) => this.cloudProxyDispatcher.SendMessageBatchAsync(this.identity, inputMessages);
        public Task SetupCallMethodAsync() => this.cloudProxyDispatcher.SetupCallMethodAsync(this.identity);
        public Task SetupDesiredPropertyUpdatesAsync() => this.cloudProxyDispatcher.SetupDesiredPropertyUpdatesAsync(this.identity);
        public Task StartListening() => this.cloudProxyDispatcher.StartListening(this.identity);
        public Task UpdateReportedPropertiesAsync(IMessage reportedPropertiesMessage) => this.cloudProxyDispatcher.UpdateReportedPropertiesAsync(this.identity, reportedPropertiesMessage);
    }
}
