using System;
using System.Threading;
using Foundation.Messaging;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Foundation.DI;

namespace Foundation.Tests.Messaging
{
    public sealed class QueryBusTests
    {
        private sealed class TestQuery { }

        [Test]
        public void Query_ReturnsHandlerResult()
        {
            var bus = new QueryBus();
            bus.RegisterHandler<TestQuery, int>(new RecordingHandler());

            var result = bus.QueryAsync<TestQuery, int>(new TestQuery()).GetAwaiter().GetResult();

            Assert.That(result, Is.EqualTo(42));
        }

        [Test]
        public void QueryWithoutHandler_FailsClearly()
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await new QueryBus().QueryAsync<TestQuery, int>(new TestQuery()));
        }

        [Test]
        public void DuplicateHandlerRegistration_FailsClearly()
        {
            var bus = new QueryBus();
            bus.RegisterHandler<TestQuery, int>(new RecordingHandler());

            Assert.Throws<InvalidOperationException>(() =>
                bus.RegisterHandler<TestQuery, int>(new RecordingHandler()));
        }

        [Test]
        public void AQueryHandlerRegisteredInAScope_AnswersWhileTheScopeLives_AndFailsTheQueryAfterwards()
        {
            using var application = MinimalServiceScope.Create(registry => registry.RegisterMessaging());
            var bus = application.Resolve<IQueryBus>();

            using (application.CreateScope(registry => registry.RegisterQueryHandler<TestQuery, int, RecordingHandler>()))
                Assert.That(bus.QueryAsync<TestQuery, int>(new TestQuery()).GetAwaiter().GetResult(), Is.EqualTo(42));

            Assert.ThrowsAsync<InvalidOperationException>(async () => await bus.QueryAsync<TestQuery, int>(new TestQuery()));
        }

        private sealed class RecordingHandler : IQueryHandler<TestQuery, int>
        {
            public UniTask<int> HandleAsync(TestQuery query, CancellationToken cancellationToken = default)
                => UniTask.FromResult(42);
        }
    }
}
