using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Foundation.GameFramework.Connectivity;
using Foundation.Scheduling;
using NUnit.Framework;

namespace Foundation.GameFramework.Tests.Connectivity
{
    public sealed class UnityConnectivityServiceTests
    {
        // Regression: a health check that completed after Dispose used to publish one more state change.
        [Test]
        public void Dispose_CancelsTheHealthCheck_AndSuppressesALateRecompute()
        {
            var service = new UnityConnectivityService(new FakeScheduler(), "https://example.invalid/health");
            var initial = service.State;
            var changes = new List<ConnectivityState>();
            service.StateChanges.Subscribe(changes.Add);
            SetField(service, "_hasNetworkInterface", true);
            SetField(service, "_healthCheckOk", true);
            var cancellation = ((CancellationTokenSource)GetField(service, "_lifetime")).Token;

            service.Dispose();
            service.Dispose();

            Invoke(service, "Recompute");

            Assert.That(cancellation.IsCancellationRequested, Is.True);
            Assert.That(service.State, Is.EqualTo(initial), "a late health check must not change state after Dispose");
            Assert.That(changes, Is.EqualTo(new[] { initial }), "no state notification may follow Dispose");
        }

        [Test]
        public void SubscribingAfterDispose_Throws_ButTheStateStaysReadable()
        {
            var service = new UnityConnectivityService(new FakeScheduler(), "https://example.invalid/health");
            var stateChanges = service.StateChanges;
            var values = new List<ConnectivityState>();
            var subscription = stateChanges.Subscribe(values.Add);
            var initial = service.State;

            service.Dispose();
            service.Dispose();

            Assert.That(service.State, Is.EqualTo(initial), "State must stay readable after Dispose");
            Assert.That(stateChanges.Value, Is.EqualTo(initial), "a retained StateChanges reference must stay readable");
            Assert.Throws<ObjectDisposedException>(() => stateChanges.Subscribe(values.Add));
            Assert.Throws<ObjectDisposedException>(() => service.StateChanges.Subscribe(values.Add));
            Assert.That(values, Is.EqualTo(new[] { initial }), "a rejected subscription must not invoke its callback");
            Assert.That(() => subscription.Dispose(), Throws.Nothing);
        }

        private static object GetField(object target, string name)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static void Invoke(object target, string name)
            => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

        private sealed class FakeScheduler : IScheduler
        {
            public IScheduledHandle ScheduleOnce(float delaySeconds, Action callback) => new Handle();

            public IScheduledHandle ScheduleRepeating(float intervalSeconds, Action callback) => new Handle();

            private sealed class Handle : IScheduledHandle
            {
                public bool IsActive => true;

                public void Dispose()
                {
                }
            }
        }
    }
}
