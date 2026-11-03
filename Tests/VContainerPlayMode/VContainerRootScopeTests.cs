using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Foundation.DI;
using Foundation.Lifecycle;
using Foundation.Pooling;
using Foundation.Scheduling;
using Foundation.Timing;
using Foundation.Infrastructure.VContainer;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;
using Object = UnityEngine.Object;

namespace Foundation.Infrastructure.VContainer.PlayModeTests
{
    public sealed class VContainerRootScopeTests
    {
        private sealed class Probe
        {
            public int RootRuns;
            public int Starts;
            public int AsyncStarts;
            public int Ticks;
            public int Disposals;
            public bool ClientBound;
            public CancellationToken RootToken;
        }

        private sealed class TestEntryPoint : RootEntryPoint
        {
            private readonly Probe _probe;

            public TestEntryPoint(Probe probe) => _probe = probe;

            protected override async UniTask RunAsync(CancellationToken cancellation)
            {
                _probe.RootRuns++;
                _probe.RootToken = cancellation;
                await UniTask.Yield();
            }
        }

        private sealed class LifecycleProbe : IStartableEntryPoint, IAsyncStartableEntryPoint, ITickableEntryPoint, IDisposable
        {
            private readonly Probe _probe;

            public LifecycleProbe(Probe probe) => _probe = probe;

            public void Start() => _probe.Starts++;

            public UniTask StartAsync(CancellationToken cancellationToken)
            {
                _probe.AsyncStarts++;
                return UniTask.CompletedTask;
            }

            public void Tick() => _probe.Ticks++;

            public void Dispose() => _probe.Disposals++;
        }

        private sealed class ProbeInstaller : IFeatureInstaller
        {
            private readonly Probe _probe;

            public ProbeInstaller(Probe probe) => _probe = probe;

            public void Install(IServiceRegistry registry)
            {
                registry.RegisterInstance(_probe);
                registry.RegisterEntryPoint<LifecycleProbe>();
            }
        }

        private sealed class TestRootScope : VContainerRootScope<TestEntryPoint>
        {
            [NonSerialized] public Probe Probe;

            protected override IReadOnlyList<IFeatureInstaller> FeatureInstallers => new IFeatureInstaller[] { new ProbeInstaller(Probe) };

            protected override void PrebindActions(IServiceRegistry registry)
            {
            }
        }

        [UnityTest]
        public IEnumerator TheRootScope_RunsTheEntryPoint_StartsAndTicksEntryPoints_AndStopsThemWithTheScope()
        {
            var probe = new Probe();
            var host = new GameObject("test root scope");
            host.SetActive(false);
            host.AddComponent<TestRootScope>().Probe = probe;

            host.SetActive(true);
            yield return null;
            yield return null;
            yield return null;

            Assert.That(probe.RootRuns, Is.EqualTo(1), "the root entry point runs once");
            Assert.That(probe.Starts, Is.EqualTo(1));
            Assert.That(probe.AsyncStarts, Is.EqualTo(1));
            Assert.That(probe.Ticks, Is.GreaterThan(0), "the ticking entry point is driven by the player loop");

            Object.Destroy(host);
            yield return null;
            var ticksAtDestroy = probe.Ticks;
            yield return null;
            yield return null;

            Assert.That(probe.Disposals, Is.EqualTo(1), "the scope disposes what it owns");
            Assert.That(probe.Ticks, Is.EqualTo(ticksAtDestroy), "a disposed scope no longer ticks its entry points");
            Assert.That(probe.RootToken.IsCancellationRequested, Is.True, "the root entry point is cancelled with its scope");
        }

        [Test]
        public void AnEntryPoint_IsResolvableAsItselfAndThroughEveryInterface_AsTheApplicationSchedulerIs()
        {
            using var scope = Build(registry =>
            {
                registry.Register<SystemTimeProvider>(ServiceLifetime.Singleton).As<ITimeProvider>();
                registry.RegisterScheduler();
            });

            var scheduler = scope.Resolve<Scheduler>();

            Assert.That(scope.Resolve<IScheduler>(), Is.SameAs(scheduler));
            Assert.That(scope.Resolve<IFixedRateScheduler>(), Is.SameAs(scheduler),
                "the entry point is resolvable through the capability it implements");
        }

        [Test]
        public void AContextSchedulerRegistration_ShadowsTheApplicationScheduler_AndLeavesWithItsScope()
        {
            using var application = Build(registry =>
            {
                registry.Register<SystemTimeProvider>(ServiceLifetime.Singleton).As<ITimeProvider>();
                registry.RegisterScheduler();
            });

            IScheduler contextScheduler;
            IScheduledHandle contextOnce;
            IScheduledHandle contextRate;
            using (var context = application.CreateScope(registry => registry.RegisterScopedScheduler()))
            {
                contextScheduler = context.Resolve<IScheduler>();
                var contextRateScheduler = context.Resolve<IFixedRateScheduler>();

                Assert.That(contextScheduler, Is.TypeOf<ScopedScheduler>());
                Assert.That(contextRateScheduler, Is.SameAs(contextScheduler),
                    "both capabilities must resolve to the same scoped wrapper");

                contextOnce = contextScheduler.ScheduleOnce(1f, () => { });
                contextRate = contextRateScheduler.ScheduleFixedRateRepeating(1f, () => { }, 1, FixedRateBacklogPolicy.Bounded);
            }

            Assert.That(application.Resolve<IScheduler>(), Is.TypeOf<Scheduler>());
            Assert.That(contextOnce.IsActive, Is.False, "the scope cancels a timer scheduled through IScheduler");
            Assert.That(contextRate.IsActive, Is.False, "the scope cancels a timer scheduled through IFixedRateScheduler");
            Assert.Throws<ObjectDisposedException>(() => contextScheduler.ScheduleOnce(1f, () => { }));
        }

        [Test]
        public void ThePoolingInstaller_RegistersThePoolServiceThroughTheScopesGameObjectInjector()
        {
            using var scope = Build(registry => new PoolingInstaller().Install(registry));

            Assert.That(scope.Resolve<IPoolService>(), Is.TypeOf<PoolService>());
        }

        private static IServiceScope Build(Action<IServiceRegistry> configure)
        {
            var builder = new ContainerBuilder();
            configure(builder.AsServiceRegistry());
            return builder.Build().AsOwningServiceScope();
        }
    }
}
