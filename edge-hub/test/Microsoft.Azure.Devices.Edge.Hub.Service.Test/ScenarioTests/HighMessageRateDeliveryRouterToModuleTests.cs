// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Hub.Service.Test.ScenarioTests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.Azure.Devices.Edge.Util.Test.Common;

    [Scenario]
    public class HighMessageRateDeliveryRouterToModuleTests
    {
        private const int BigPack = 10000;
        private const int MidPack = 1000;
        private const int SmallPack = 10;

        [RunnableInDebugOnly]
        public async Task SendWithNoError()
        {
            var deliverable = StandardDeliverable
                                .Create()
                                .WithPackSize(BigPack)
                                .WithTimingStrategy<NoWaitTimingStrategy>();

            var moduleProxy = FlakyDeviceProxy
                                .Create()
                                .WithSendOutAction(deliverable.ConfirmDelivery)
                                .WithThrowTimeStrategy<DoNotThrowStrategy>();

            var router = RouterBuilder
                            .Create()
                            .WithRoute(route => route.WithModuleProxy(moduleProxy))
                            .Build();

            await deliverable.StartDeliveringAsync(router);
            await deliverable.WaitTillAllDeliveredAsync(this.TimeoutToken(TimeSpan.FromMinutes(20)));

            deliverable.DeliveredJournal.EnsureOrdered();
        }

        // TODO: this test runs really slow, figure out why and if it is possible to improve perf - maybe related to retry
        [RunnableInDebugOnly]
        public async Task SendWithRetriableErrors()
        {
            var deliverable = StandardDeliverable
                                .Create()
                                .WithPackSize(BigPack)
                                .WithTimingStrategy<NoWaitTimingStrategy>();

            var moduleProxy = FlakyDeviceProxy
                                .Create()
                                .WithSendOutAction(deliverable.ConfirmDelivery)
                                .WithException<Core.EdgeHubIOException>()
                                .WithThrowTimeStrategy<RandomThrowTimeStrategy>(throwing => throwing.WithOddsToThrow(0.2)); // 20% that throws

            var router = RouterBuilder
                            .Create()
                            .WithRoute(route => route.WithModuleProxy(moduleProxy))
                            .Build();

            await deliverable.StartDeliveringAsync(router);
            await deliverable.WaitTillAllDeliveredAsync(this.TimeoutToken(TimeSpan.FromMinutes(60)));

            deliverable.DeliveredJournal.EnsureOrdered();
        }

        [RunnableInDebugOnly]
        public async Task SendWithNonRetriableErrors()
        {
            var deliverable = StandardDeliverable
                                .Create()
                                .WithPackSize(BigPack)
                                .WithTimingStrategy<NoWaitTimingStrategy>();

            var moduleProxy = FlakyDeviceProxy
                                .Create()
                                .WithSendOutAction(deliverable.ConfirmDelivery)
                                .WithThrowingAction((msg, ex) => deliverable.DontExpectDelivery(msg))
                                .WithException<Exception>()
                                .WithThrowTimeStrategy<RandomThrowTimeStrategy>(throwing => throwing.WithOddsToThrow(0.2)); // 20% that throws

            var router = RouterBuilder
                            .Create()
                            .WithRoute(route => route.WithModuleProxy(moduleProxy))
                            .Build();

            await deliverable.StartDeliveringAsync(router);
            await deliverable.WaitTillAllDeliveredAsync(this.TimeoutToken(TimeSpan.FromMinutes(10)));

            deliverable.DeliveredJournal.EnsureOrderedWithGaps();
        }

        // TODO: this test runs really slow, figure out why and if it is possible to improve perf - maybe related to retry
        [RunnableInDebugOnly]
        public async Task SendWithMixedErrors()
        {
            var retriableExceptions = new HashSet<Type>
            {
                typeof(TimeoutException),
                typeof(IOException),
                typeof(Core.EdgeHubIOException)
            };

            var nonRetriableExceptions = new HashSet<Type>
            {
                typeof(Exception),
                typeof(NullReferenceException),
                typeof(ArgumentNullException)
            };

            var deliverable = StandardDeliverable
                                .Create()
                                .WithPackSize(BigPack)
                                .WithTimingStrategy<NoWaitTimingStrategy>();

            Action<Core.IMessage, Exception> handleExceptions =
                (msg, ex) =>
                {
                    if (!retriableExceptions.Contains(ex.GetType()))
                        deliverable.DontExpectDelivery(msg);
                };

            var moduleProxy = FlakyDeviceProxy
                                .Create()
                                .WithSendOutAction(deliverable.ConfirmDelivery)
                                .WithThrowingAction(handleExceptions)
                                .WithThrowTypeStrategy<MultiThrowTypeStrategy>(
                                    exception => exception.WithExceptionSuite(retriableExceptions)
                                                          .WithExceptionSuite(nonRetriableExceptions))
                                .WithThrowTimeStrategy<RandomThrowTimeStrategy>(
                                    throwing => throwing.WithOddsToThrow(0.2));

            var router = RouterBuilder
                            .Create()
                            .WithRoute(route => route.WithModuleProxy(moduleProxy))
                            .Build();

            await deliverable.StartDeliveringAsync(router);
            await deliverable.WaitTillAllDeliveredAsync(this.TimeoutToken(TimeSpan.FromMinutes(60)));

            deliverable.DeliveredJournal.EnsureOrderedWithGaps();
        }

        [RunnableInDebugOnly]
        public async Task SendWithSlowReceivingSide()
        {
            var deliverable = StandardDeliverable
                                .Create()
                                .WithPackSize(MidPack)
                                .WithTimingStrategy<NoWaitTimingStrategy>();

            var moduleProxy = FlakyDeviceProxy
                                .Create()
                                .WithSendOutAction(deliverable.ConfirmDelivery)
                                .WithSendDelay(1000, 500) // a packet is completed in 0.5-1.5 seconds
                                .WithThrowTimeStrategy<DoNotThrowStrategy>();

            var router = RouterBuilder
                            .Create()
                            .WithRoute(route => route.WithModuleProxy(moduleProxy))
                            .Build();

            await deliverable.StartDeliveringAsync(router);
            await deliverable.WaitTillAllDeliveredAsync(this.TimeoutToken(TimeSpan.FromMinutes(60)));

            deliverable.DeliveredJournal.EnsureOrdered();
        }

        [RunnableInDebugOnly]
        public async Task SendWithLaggingReceivingSide()
        {
            var deliverable = StandardDeliverable
                                .Create()
                                .WithPackSize(SmallPack) // careful, this test waits a lot
                                .WithTimingStrategy<NoWaitTimingStrategy>();

            var moduleProxy = FlakyDeviceProxy
                                .Create()
                                .WithSendOutAction(deliverable.ConfirmDelivery)
                                .WithThrowTimeStrategy<DoNotThrowStrategy>()
                                .WithSendDelayStrategy<RandomLaggingTimingStrategy>(
                                        delay => delay.WithDelay(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(4))
                                                    .WithOddsToGetStuck(0.05) // lag at every ~20th send in average
                                                    .WithBaseStrategy<LinearTimingStrategy>(
                                                        baseDelay => baseDelay.WithDelay(300, 100))); // 0.2-0.4 sec normal delay

            var router = RouterBuilder
                            .Create()
                            .WithRoute(route => route.WithModuleProxy(moduleProxy))
                            .Build();

            await deliverable.StartDeliveringAsync(router);
            await deliverable.WaitTillAllDeliveredAsync(this.TimeoutToken(TimeSpan.FromMinutes(60)));

            deliverable.DeliveredJournal.EnsureOrdered();
        }

        [RunnableInDebugOnly]
        public async Task SendBigMessages()
        {
            var deliverable = StandardDeliverable
                                .Create()
                                .WithPackSize(SmallPack)
                                .WithMessageGeneratingStrategy<SimpleMessageGeneratingStrategy>(
                                    message => message.WithBodySize((int)Core.Constants.MaxMessageSize / 4, (int)Core.Constants.MaxMessageSize))
                                .WithTimingStrategy<NoWaitTimingStrategy>();

            var moduleProxy = FlakyDeviceProxy
                                .Create()
                                .WithSendOutAction(deliverable.ConfirmDelivery)
                                .WithThrowTimeStrategy<DoNotThrowStrategy>();

            var router = RouterBuilder
                            .Create()
                            .WithRoute(route => route.WithModuleProxy(moduleProxy))
                            .Build();

            await deliverable.StartDeliveringAsync(router);
            await deliverable.WaitTillAllDeliveredAsync(this.TimeoutToken(TimeSpan.FromMinutes(20)));

            deliverable.DeliveredJournal.EnsureOrdered();
        }

        private CancellationToken TimeoutToken(TimeSpan timeSpan)
        {
            var tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(timeSpan);

            return tokenSource.Token;
        }
    }
}
