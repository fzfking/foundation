using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Foundation.Infrastructure.Addressables.Tests
{
    public sealed class AddressablesContentInitializerTests
    {
        [UnityTest]
        public IEnumerator MultipleCallersShareInitializationAndCancelTheirOwnWait() => UniTask.ToCoroutine(async () =>
        {
            var checker = new WaitingReachabilityChecker();
            using var initializer = new AddressablesContentInitializer(checker);
            using var firstCancellation = new CancellationTokenSource();
            using var secondCancellation = new CancellationTokenSource();

            var first = initializer.InitializeAsync(firstCancellation.Token);
            var second = initializer.InitializeAsync();
            var canceledWait = initializer.InitializeAsync(secondCancellation.Token);
            secondCancellation.Cancel();

            Assert.That(await canceledWait.SuppressCancellationThrow(), Is.True);
            Assert.That(first.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(second.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(checker.CallCount, Is.EqualTo(1));

            firstCancellation.Cancel();
            Assert.That(await first.SuppressCancellationThrow(), Is.True);
            Assert.That(await second.SuppressCancellationThrow(), Is.True);
            Assert.That(await initializer.InitializeAsync().SuppressCancellationThrow(), Is.True);
            Assert.That(checker.CallCount, Is.EqualTo(1));
        });

        private sealed class WaitingReachabilityChecker : IContentHostReachabilityChecker
        {
            private readonly UniTaskCompletionSource<bool> _completion = new();

            public int CallCount { get; private set; }

            public UniTask<bool> IsReachableAsync(CancellationToken cancellationToken = default)
            {
                CallCount++;
                return _completion.Task.AttachExternalCancellation(cancellationToken);
            }
        }
    }
}
