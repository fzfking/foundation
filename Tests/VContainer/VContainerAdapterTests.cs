using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.DI;
using Foundation.GameContexts;
using Foundation.Messaging;
using Foundation.Simulation;
using Foundation.Timing;
using Foundation.Infrastructure.VContainer;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;

namespace Foundation.Infrastructure.VContainer.Tests
{
    public sealed class VContainerAdapterTests
    {
        private interface IGreeter
        {
            string Greet();
        }

        private sealed class Greeter : IGreeter, IDisposable
        {
            public int Disposed;

            public string Greet() => "hello";

            public void Dispose() => Disposed++;
        }

        private sealed class AnotherGreeter : IGreeter
        {
            public string Greet() => "hi";
        }

        private sealed class NativeInstaller : IFeatureInstaller
        {
            public void Install(IServiceRegistry registry) =>
                registry.ToContainerBuilder().Register<Greeter>(Lifetime.Singleton).As<IGreeter>();
        }

        private sealed class Consumer
        {
            public Consumer(IGreeter greeter) => Greeter = greeter;

            public IGreeter Greeter { get; }
        }

        [Test]
        public void ARegistration_IsExposedAsItsOwnTypeUntilAsSaysOtherwise()
        {
            using var scope = Build(registry =>
            {
                registry.Register<Greeter>(ServiceLifetime.Singleton).As<IGreeter>();
            });

            Assert.That(scope.Resolve<IGreeter>().Greet(), Is.EqualTo("hello"));
            Assert.That(scope.TryResolve<Greeter>(out _), Is.False, "As<> replaced the default self registration");
        }

        [Test]
        public void ACollectionParameter_ReceivesEveryRegistrationOfTheService()
        {
            using var scope = Build(registry =>
            {
                registry.Register<Greeter>(ServiceLifetime.Singleton).As<IGreeter>();
                registry.Register<AnotherGreeter>(ServiceLifetime.Singleton).As<IGreeter>();
            });

            Assert.That(scope.Resolve<IReadOnlyList<IGreeter>>().Count, Is.EqualTo(2));
        }

        private sealed class CountingSystem : ISimulationSystem
        {
            public int Order => 0;

            public void Tick(in SimulationTick tick)
            {
            }
        }

        [Test]
        public void UseSimulation_BuildsTheRunnerOverEverySystemRegisteredWithUseSimulationSystem()
        {
            using var scope = Build(registry => registry
                .UseTime()
                .UseSimulation()
                .UseSimulationSystem<CountingSystem>());

            var systems = scope.Resolve<IReadOnlyList<ISimulationSystem>>();

            Assert.That(scope.Resolve<SimulationRunner>(), Is.Not.Null);
            Assert.That(systems.Count, Is.EqualTo(1));
            Assert.That(systems[0], Is.SameAs(scope.Resolve<CountingSystem>()));
        }

        [Test]
        public void AnInstaller_MayReachVContainersFullApi_ThroughToContainerBuilder()
        {
            using var scope = Build(registry => new NativeInstaller().Install(registry));

            Assert.That(scope.Resolve<IGreeter>().Greet(), Is.EqualTo("hello"));
        }

        [Test]
        public void AChildScope_ShadowsItsParent_OwnsItsSingletons_AndLeavesTheParentUntouched()
        {
            using var application = Build(registry => registry.Register<Greeter>(ServiceLifetime.Singleton).As<IGreeter>());
            var parentGreeter = (Greeter)application.Resolve<IGreeter>();

            var child = application.CreateScope(registry => registry.Register<Greeter>(ServiceLifetime.Singleton).As<IGreeter>());
            var childGreeter = (Greeter)child.Resolve<IGreeter>();
            Assert.That(childGreeter, Is.Not.SameAs(parentGreeter));

            child.Dispose();

            Assert.That(childGreeter.Disposed, Is.EqualTo(1));
            Assert.That(parentGreeter.Disposed, Is.EqualTo(0));
            Assert.That(application.Resolve<IGreeter>(), Is.SameAs(parentGreeter));
        }

        [Test]
        public void AFactoryRegistration_ReceivesTheResolverOfItsScope_AndOptionalServicesUseTryResolve()
        {
            using var scope = Build(registry =>
            {
                registry.Register<IGreeter>(_ => new Greeter(), ServiceLifetime.Singleton);
                registry.Register(
                    resolver => new Consumer(resolver.TryResolve(out IGreeter greeter) ? greeter : null),
                    ServiceLifetime.Singleton);
            });

            Assert.That(scope.Resolve<Consumer>().Greeter, Is.SameAs(scope.Resolve<IGreeter>()));
        }

        [Test]
        public void BuildAndDisposeCallbacks_RunOncePerScope()
        {
            var events = new List<string>();
            var scope = Build(registry =>
            {
                registry.RegisterBuildCallback(_ => events.Add("built"));
                registry.RegisterDisposeCallback(_ => events.Add("disposed"));
            });

            Assert.That(events, Is.EqualTo(new[] { "built" }));

            scope.Dispose();

            Assert.That(events, Is.EqualTo(new[] { "built", "disposed" }));
        }

        [Test]
        public void ACommandHandler_LeavesWithTheScopeThatRegisteredIt_AndTheScopeCanBeEnteredAgain()
        {
            using var application = Build(registry => registry.RegisterMessaging());
            var bus = application.Resolve<ICommandBus>();

            Handler first;
            using (var context = application.CreateScope(registry => registry.RegisterCommandHandler<Ping, Handler>()))
            {
                first = context.Resolve<Handler>();
                bus.SendAsync(new Ping()).GetAwaiter().GetResult();
            }

            Assert.ThrowsAsync<InvalidOperationException>(async () => await bus.SendAsync(new Ping()));

            using var reentered = application.CreateScope(registry => registry.RegisterCommandHandler<Ping, Handler>());
            bus.SendAsync(new Ping()).GetAwaiter().GetResult();

            Assert.That(first.Calls, Is.EqualTo(1));
            Assert.That(reentered.Resolve<Handler>().Calls, Is.EqualTo(1));
        }

        [Test]
        public void TheGameObjectInjector_HandsTheScopesResolverToEveryResolverClientUnderTheObject()
        {
            using var scope = Build(registry => registry.Register<IGreeter>(_ => new Greeter(), ServiceLifetime.Singleton));
            var root = new GameObject("root");
            var child = new GameObject("child");
            child.transform.SetParent(root.transform);
            child.SetActive(false);
            var client = child.AddComponent<ResolverClient>();

            try
            {
                scope.Resolve<IGameObjectInjector>().Inject(root);

                Assert.That(client.Greeter, Is.SameAs(scope.Resolve<IGreeter>()), "inactive children are bound too");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TheGameContextScopeFactory_BuildsAChildThatSeesTheApplicationAndOwnsTheContext()
        {
            using var application = Build(registry => registry.Register<Greeter>(ServiceLifetime.Singleton).As<IGreeter>());
            var factory = new VContainerGameContextScopeFactory(((VContainerServiceScope)application).Resolver);
            var definition = new GameContextDefinition(
                new GameContextId("main"),
                new GameContextSaveProfile("main", 1),
                new IFeatureInstaller[] { new MarkerInstaller() });

            using var context = factory.CreateScope(definition);

            Assert.That(context.Resolve<GameContextDefinition>(), Is.SameAs(definition));
            Assert.That(context.Resolve<GameContextSaveProfile>(), Is.SameAs(definition.Save));
            Assert.That(context.Resolve<IGreeter>(), Is.SameAs(application.Resolve<IGreeter>()));
            Assert.That(context.Resolve<Marker>(), Is.Not.Null);
            Assert.That(application.TryResolve<Marker>(out _), Is.False);
        }

        private static IServiceScope Build(Action<IServiceRegistry> configure)
        {
            var builder = new ContainerBuilder();
            configure(builder.AsServiceRegistry());
            return builder.Build().AsOwningServiceScope();
        }

        private sealed class Ping
        {
        }

        private sealed class Handler : ICommandHandler<Ping>
        {
            public int Calls;

            public UniTask HandleAsync(Ping command, CancellationToken cancellationToken = default)
            {
                Calls++;
                return UniTask.CompletedTask;
            }
        }

        private sealed class Marker
        {
        }

        private sealed class MarkerInstaller : IFeatureInstaller
        {
            public void Install(IServiceRegistry registry) => registry.Register<Marker>(ServiceLifetime.Singleton);
        }

        private sealed class ResolverClient : MonoBehaviour, IServiceResolverClient
        {
            public IGreeter Greeter { get; private set; }

            public void Bind(IServiceResolver resolver) => Greeter = resolver.Resolve<IGreeter>();
        }
    }
}
