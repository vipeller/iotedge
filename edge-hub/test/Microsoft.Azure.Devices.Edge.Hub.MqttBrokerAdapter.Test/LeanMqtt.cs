namespace Microsoft.Azure.Devices.Edge.Hub.MqttBrokerAdapter.Test
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Sockets;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public class LeanMqtt
    {
        const int CONNECT = 1;
        const int PUBLISH = 3;
        const int SUBSCRIBE = 8;
        const int PUBACK = 4;

        readonly int port;

        int messageCntr = 1;

        ConcurrentQueue<MqttClientAction> clientActions = new ConcurrentQueue<MqttClientAction>();
        NetworkStream clientStream;

        public LeanMqtt(int port)
        {
            this.port = port;
        }

        public Task Start(CancellationToken token) => _ = Task.Run(() => ListenerLoopAsync(token));

        public IReadOnlyCollection<MqttClientAction> ClientActions => this.clientActions;

        public async Task<bool> PublishMessage(string topic, byte[] payload)
        {
            // 2 bytes topic len, the topic, 2 bytes message id, payload:
            var length = 2 + topic.Length + 2 + payload.Length;
            var encodedLength = GetVariableInteger(length);

            // the total packet is 1 byte control, encoded len, and the length calculated abouve
            var packet = new byte[1 + length + encodedLength.Length];
            var pos = 0;

            // qos1
            packet[pos++] = 0x32;

            foreach (var b in encodedLength)
            {
                packet[pos++] = b;
            }

            packet[pos++] = (byte)(topic.Length >> 8);
            packet[pos++] = (byte)topic.Length;

            foreach (var b in Encoding.ASCII.GetBytes(topic))
            {
                packet[pos++] = b;
            }

            packet[pos++] = (byte)(messageCntr >> 8);
            packet[pos++] = (byte)messageCntr;

            messageCntr = (++messageCntr) & 0x7fff;

            foreach (var b in payload)
            {
                packet[pos++] = b;
            }

            await this.clientStream.WriteAsync(packet, 0, packet.Length);

            return true;
        }

        public Task<bool> WaitActionAsync(Func<IReadOnlyCollection<MqttClientAction>, bool> predicate, TimeSpan timeout)
        {
            var result = new TaskCompletionSource<bool>();

            _ = Task.Run(
                async () =>
                {
                    var startTime = DateTime.Now;

                    while (DateTime.Now.Subtract(startTime) < timeout)
                    {
                        if (predicate(this.ClientActions))
                        {
                            result.SetResult(true);
                            return;
                        }

                        await Task.Delay(200);
                    }

                    result.SetResult(false);
                }
            );

            return result.Task;
        }

        public ActionChain StartScenario()
        {
            return new ActionChain(this, () => Task.FromResult(true));
        }

        public Task<bool> CheckClientConnected(TimeSpan timeout)
        {
            return WaitActionAsync(a => a.Any(i => i.Type == MqttClientAction.ActionType.Connect), timeout);
        }

        public Task<bool> CheckClientSubscribed(string topic, TimeSpan timeout)
        {
            return WaitActionAsync(a => a.Any(i => (i is SubscribeAction) && (i as SubscribeAction).Subscriptions.Contains(topic)), timeout);
        }

        public Task<bool> CheckClientPublished(string topic, byte[] payload, TimeSpan timeout)
        {
            return WaitActionAsync(
                        a => a.Any(i => (i is PublishAction) &&
                                        (i as PublishAction).Topic == topic &&
                                        (i as PublishAction).Payload.SequenceEqual(payload)),
                        timeout);
        }

        async Task ListenerLoopAsync(CancellationToken token)
        {
            try
            {
                var server = new TcpListener(IPAddress.Loopback, this.port);

                server.Start();

                // because not all TcpListener operations have timeout/cancellation,
                // we will stop it forcefully to stop the entire server
                token.Register(server.Stop);

                var client = await server.AcceptTcpClientAsync();
                this.clientStream = client.GetStream();
                this.clientStream.ReadTimeout = 15000;

                var incomingBuffer = new byte[64 * 1024];
                var bufferStart = 0;

                do
                {
                    // doing this loop because the cancellation token and read timeout don't seem working
                    // on network-stream, and it blocks test-execution for a minute before timeouts
                    if (clientStream.DataAvailable)
                    {
                        var bytesReceived = await clientStream.ReadAsync(incomingBuffer, bufferStart, incomingBuffer.Length - bufferStart, token);
                        var chunkLeft = await ProcessIncomingAsync(incomingBuffer, bufferStart + bytesReceived);

                        if (chunkLeft > 0)
                        {
                            Array.Copy(incomingBuffer, bufferStart + bytesReceived - chunkLeft, incomingBuffer, 0, chunkLeft);
                            bufferStart = chunkLeft;
                        }
                    }
                    else
                    {
                        await Task.Delay(100);
                    }
                }
                while (!token.IsCancellationRequested);

                // no need to stop the server here: the token-callback stopped when got cancelled
            }
            finally
            {
                if (this.clientStream != null)
                {
                    this.clientStream.Dispose();
                }
            }
        }

        async Task<int> ProcessIncomingAsync(byte[] incomingBuffer, int length)
        {
            int pos = 0;

            while (pos < length)
            {
                var processed = default(int);
                switch (incomingBuffer[0] >> 4)
                {
                    case CONNECT:
                        processed = await HandleConnectAsync(incomingBuffer, pos, length);
                        break;

                    case SUBSCRIBE:
                        processed = await HandleSubscribeAsync(incomingBuffer, pos, length);
                        break;

                    case PUBLISH:
                        processed = await HandlePublishAsync(incomingBuffer, pos, length);
                        break;

                    case PUBACK:
                        processed = await HandlePubackAsync(incomingBuffer, pos, length);
                        break;

                    default:
                        break;
                }

                if (processed == 0)
                {
                    return length - pos;
                }
                else
                {
                    pos += processed;
                }
            }

            return 0;
        }

        async Task<int> HandleConnectAsync(byte[] incomingBuffer, int pos, int length)
        {
            // Don't care about anything, just send back a connack
            var packetStart = pos;

            // skip control byte, check if we fit
            if (++pos >= length)
            {
                return 0;
            }

            // we assume that the packet is small enought that len fits in 1 byte
            var len = incomingBuffer[pos++];
            var packetSize = 2 + len;

            if (packetSize > length - packetStart)
            {
                return 0;
            }

            var connack = new byte[] { 0x20, 0x02, 0x00, 0x00 };

            await this.SendAsync(connack);

            this.clientActions.Enqueue(new ConnectAction());

            return packetSize;
        }

        async Task<int> HandleSubscribeAsync(byte[] incomingBuffer, int pos, int length)
        {
            var packetStart = pos;

            // skip control byte, check if we fit
            if (++pos >= length)
            {
                return 0;
            }

            var bytesRead = default(int);
            var remainingLen = ReadVariableLengthInteger(incomingBuffer, pos, length, out bytesRead);

            if (bytesRead == 0)
            {
                return 0;
            }

            pos += bytesRead;

            var packetEnd = pos + remainingLen;

            if (packetEnd > length)
            {
                return 0;
            }

            // subscription id
            var idHigh = incomingBuffer[pos++];
            var idLow = incomingBuffer[pos++];

            // keep reading the topic-qos pairs still we have anything left
            var topics = new List<string>();
            var qos = new List<int>();
            while (pos < packetEnd)
            {
                var topicLen = (incomingBuffer[pos++] << 8) + incomingBuffer[pos++];
                var topic = Encoding.UTF8.GetString(incomingBuffer, pos, topicLen);

                pos += topicLen;

                topics.Add(topic);
                qos.Add(incomingBuffer[pos++]);
            }

            var suback = new byte[4 + qos.Count];
            suback[0] = 0x90;
            suback[1] = (byte)(2 + qos.Count);
            suback[2] = idHigh;
            suback[3] = idLow;

            for (var i = 0; i < qos.Count; i++)
            {
                suback[4 + i] = (byte)qos[i];
            }

            await this.SendAsync(suback);

            this.clientActions.Enqueue(new SubscribeAction(topics));

            return packetEnd - packetStart;
        }

        async Task<int> HandlePublishAsync(byte[] incomingBuffer, int pos, int length)
        {
            var packetStart = pos;

            // skip control byte, check if we fit
            if (++pos >= length)
            {
                return 0;
            }

            var bytesRead = default(int);
            var remainingLen = ReadVariableLengthInteger(incomingBuffer, pos, length, out bytesRead);

            if (bytesRead == 0)
            {
                return 0;
            }

            pos += bytesRead;

            var packetEnd = pos + remainingLen;

            if (packetEnd > length)
            {
                return 0;
            }

            var topicLen = (incomingBuffer[pos++] << 8) + incomingBuffer[pos++];
            var topic = Encoding.UTF8.GetString(incomingBuffer, pos, topicLen);

            pos += topicLen;

            // message id
            var idHigh = incomingBuffer[pos++];
            var idLow = incomingBuffer[pos++];

            var payload = new byte[length - pos];
            Array.Copy(incomingBuffer, pos, payload, 0, length - pos);

            this.clientActions.Enqueue(new PublishAction(topic, payload));

            var ack = new byte[4];
            ack[0] = 0x40;
            ack[1] = 0x02;
            ack[2] = idHigh;
            ack[3] = idLow;

            await this.SendAsync(ack);

            return packetEnd - packetStart;
        }

        Task<int> HandlePubackAsync(byte[] incomingBuffer, int pos, int length)
        {
            // Ack is always 4 bytes and we don't care about message ids. Just eat 4 bytes.
            return Task.FromResult(length < 4 ? 0 : 4);
        }

        async Task SendAsync(byte[] encodedPackage) => await this.clientStream.WriteAsync(encodedPackage);

        int ReadVariableLengthInteger(byte[] incomingBuffer, int pos, int length, out int bytesRead)
        {
            var fieldStart = pos;

            var multiplier = 1;
            var value = default(int);
            var encodedByte = default(byte);

            bytesRead = 0;

            do
            {
                if (pos == length)
                {
                    return 0;
                }

                encodedByte = incomingBuffer[pos++];
                value += (int)((encodedByte & 127) * multiplier);

                if (multiplier > 2097152)
                {
                    throw new Exception("Variable length integer is invalid.");
                }

                multiplier *= 128;
            } while ((encodedByte & 128) != 0);

            bytesRead = pos - fieldStart;

            return value;
        }

        public byte[] GetVariableInteger(int value)
        {
            var temp = new byte[5]; // cannot be bigger;
            var offset = 0;

            var encodedByte = default(byte);

            do
            {
                encodedByte = (byte)(value % 0x80);
                value = value / 0x80;

                if (value > 0)
                {
                    encodedByte = (byte)(encodedByte | 0x80);
                }

                temp[offset++] = encodedByte;
            }
            while (value > 0);

            var result = new byte[offset];
            Array.Copy(temp, result, offset);

            return result;
        }

        public class ActionChain
        {
            readonly LeanMqtt broker;
            readonly Func<Task<bool>> item;

            public ActionChain(LeanMqtt broker, Func<Task<bool>> item)
            {
                this.broker = broker;
                this.item = item;
            }

            public ActionChain Then(Func<LeanMqtt, Task<bool>> item)
            {
                return new ActionChain(
                    this.broker,
                    async () =>
                    {
                        var result = await this.item();

                        if (result)
                        {
                            return await item(this.broker);
                        }
                        else
                        {
                            return false;
                        }
                    });
            }

            public Task<bool> Execute() => this.item();
        }
    }
}
