﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using SlimMessageBus.Host.Config;
using Xunit;

namespace SlimMessageBus.Host.Test.Consumer
{
    public class MessageQueueWorkerTest
    {
        private Mock<ConsumerInstancePool<SomeMessage>> _consumerInstancePoolMock;
        private Mock<ICheckpointTrigger> _checkpointTriggerMock;
        private MessageBusMock _busMock;

        public MessageQueueWorkerTest()
        {
            _busMock = new MessageBusMock();
            _checkpointTriggerMock = new Mock<ICheckpointTrigger>();

            var consumerSettings = new ConsumerSettings
            {
                Instances = 2,
                ConsumerMode = ConsumerMode.Subscriber,
                ConsumerType = typeof(IConsumer<SomeMessage>),
                MessageType = typeof(SomeMessage)
            };

            Func<SomeMessage, byte[]> payloadProvider = m => new byte[0];
            _consumerInstancePoolMock = new Mock<ConsumerInstancePool<SomeMessage>>(consumerSettings, _busMock.BusMock.Object, payloadProvider, null);
        }

        [Fact]
        public void Commit_WaitsOnAllMessagesToComplete()
        {
            // arrange
            //_checkpointTriggerMock.SetupGet(x => x.IsEnabled).Returns(false);
            var w = new MessageQueueWorker<SomeMessage>(_consumerInstancePoolMock.Object, _checkpointTriggerMock.Object);

            var numFinishedMessages = 0;
            _consumerInstancePoolMock.Setup(x => x.ProcessMessage(It.IsAny<SomeMessage>())).Returns(() => Task.Delay(50).ContinueWith(t => Interlocked.Increment(ref numFinishedMessages)));

            const int numMessages = 100;
            for (var i = 0; i < numMessages; i++)
            {
                w.Submit(new SomeMessage());
            }

            // act
            var success = w.WaitAll(out SomeMessage lastGoodMessage);

            // assert
            success.Should().BeTrue();
            numFinishedMessages.Should().Be(numMessages);
        }

        [Fact]
        public void Commit_IfSomeMessageFails_ReturnsFirstNonFailedMessage()
        {
            // arrange
            var w = new MessageQueueWorker<SomeMessage>(_consumerInstancePoolMock.Object, _checkpointTriggerMock.Object);

            var taskQueue = new Queue<Task>();
            taskQueue.Enqueue(Task.CompletedTask);
            taskQueue.Enqueue(Task.Delay(3000));
            taskQueue.Enqueue(Task.FromException(new Exception()));
            taskQueue.Enqueue(Task.CompletedTask);
            taskQueue.Enqueue(Task.FromException(new Exception()));
            taskQueue.Enqueue(Task.CompletedTask);

            var messages = taskQueue.ToList().Select(x => new SomeMessage()).ToArray();

            _consumerInstancePoolMock.Setup(x => x.ProcessMessage(It.IsAny<SomeMessage>())).Returns(() => taskQueue.Dequeue());

            foreach (var t in messages)
            {
                w.Submit(t);
            }

            // act
            var success = w.WaitAll(out SomeMessage lastGoodMessage);

            // assert
            success.Should().BeFalse();
            lastGoodMessage.Should().BeSameAs(messages[1]);
        }
    }
}
