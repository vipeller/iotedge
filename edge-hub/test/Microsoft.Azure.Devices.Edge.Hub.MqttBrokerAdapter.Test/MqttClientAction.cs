namespace Microsoft.Azure.Devices.Edge.Hub.MqttBrokerAdapter.Test
{
    using System.Collections.Generic;
    using System.Linq;

    public class MqttClientAction
    {
        public enum ActionType
        {
            Connect,
            Subscribe,
            Publish
        }

        public virtual ActionType Type { get; }
    }

    public class ConnectAction : MqttClientAction
    {
        public override ActionType Type => ActionType.Connect;
    }

    public class SubscribeAction : MqttClientAction
    {
        public override ActionType Type => ActionType.Subscribe;

        public SubscribeAction(IEnumerable<string> topics)
        {
            this.Subscriptions = topics.ToArray();
        }

        public string[] Subscriptions { get; }
    }

    public class PublishAction : MqttClientAction
    {
        public override ActionType Type => ActionType.Publish;

        public PublishAction(string topic, byte[] payload)
        {
            this.Topic = topic;
            this.Payload = payload;
        }

        public string Topic { get; }
        public byte[] Payload { get; }
    }
}
