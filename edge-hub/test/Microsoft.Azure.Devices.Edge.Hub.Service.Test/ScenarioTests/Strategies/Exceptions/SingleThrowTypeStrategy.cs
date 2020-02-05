// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Hub.Service.Test.ScenarioTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Azure.Devices.Client.Exceptions;
    using Microsoft.Azure.Devices.Edge.Hub.Core;

    public class SingleThrowTypeStrategy : IMessageConverter<Exception>
    {
        private Type exceptionType = typeof(IotHubException);

        public static SingleThrowTypeStrategy Create() => new SingleThrowTypeStrategy();

        public SingleThrowTypeStrategy WithType<T>()
            where T : Exception
        {
            this.exceptionType = typeof(T);
            return this;
        }

        public Exception Convert(IMessage message)
        {
            var errorMessage = "test-error";

            if (message.SystemProperties.TryGetValue(SystemProperties.MessageId, out string id))
            {
                errorMessage += $" for msg: {id}";
            }

            var toThrow = this.CreateInstance(errorMessage);
            toThrow.Data["Messages"] = new IMessage[] { message };

            return toThrow;
        }

        public Exception Convert(IEnumerable<IMessage> messages)
        {
            var errorMessage = "test-error";

            if (messages.Any() && messages.First().SystemProperties.TryGetValue(SystemProperties.MessageId, out string id))
            {
                errorMessage += $" for msg: {id}";
                if (messages.Count() > 1)
                {
                    errorMessage += $" and {messages.Count() - 1} more...";
                }
            }

            var toThrow = this.CreateInstance(errorMessage);
            toThrow.Data["Messages"] = messages;

            return toThrow;
        }

        public Exception Convert(Client.Message message)
        {
            var errorMessage = $"test-error for msg: {message.MessageId}";
            var toThrow = this.CreateInstance(errorMessage);
            toThrow.Data["Messages"] = new Client.Message[] { message };

            return toThrow;
        }

        public Exception Convert(IEnumerable<Client.Message> messages)
        {
            var errorMessage = "test-error";

            if (messages.Any())
            {
                errorMessage += $" for msg: {messages.First().MessageId}";
                if (messages.Count() > 1)
                {
                    errorMessage += $" and {messages.Count() - 1} more...";
                }
            }

            var toThrow = this.CreateInstance(errorMessage);
            toThrow.Data["Messages"] = messages;

            return toThrow;
        }

        private Exception CreateInstance(string message)
        {
            return Activator.CreateInstance(this.exceptionType, message) as Exception;
        }
    }
}