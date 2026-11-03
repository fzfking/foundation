using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.DI;
using Foundation.Lifecycle;
using NUnit.Framework;

namespace Foundation.Tests
{
    public sealed class MinimalServiceScopeTests
    {
        [Test]
        public void AsyncEntryPoint_StartsAndItsTokenIsCancelledOnDispose()
        {
            var starter = new AsyncStarter();
            var scope = MinimalServiceScope.Create(registry => registry.RegisterEntryPoint(_ => starter));
            Assert.That(starter.Started, Is.True);
            Assert.That(starter.Token.IsCancellationRequested, Is.False);

            scope.Dispose();
            Assert.That(starter.Token.IsCancellationRequested, Is.True);
        }

        [Test]
        public void Collections_IncludeParentAndChildRegistrationsInOrder()
        {
            var first = new Greeter();
            var second = new Greeter();
            using var parent = MinimalServiceScope.Create(registry => registry.RegisterInstance<IGreeter>(first));
            using var child = parent.CreateScope(registry => registry.RegisterInstance<IGreeter>(second));

            Assert.That(child.Resolve<IReadOnlyList<IGreeter>>(), Is.EqualTo(new[] { first, second }));
            Assert.That(child.Resolve<IGreeter>(), Is.SameAs(second));
        }

        [Test]
        public void OptionalConstructorDependency_UsesDefaultOnlyWhenNotRegistered()
        {
            using var missing = MinimalServiceScope.Create(registry => registry.Register<OptionalConsumer>(ServiceLifetime.Singleton));
            Assert.That(missing.Resolve<OptionalConsumer>().Greeter, Is.Null);
            var greeter = new Greeter();
            using var provided = MinimalServiceScope.Create(registry =>
            {
                registry.RegisterInstance<IGreeter>(greeter);
                registry.Register<OptionalConsumer>(ServiceLifetime.Singleton);
            });
            Assert.That(provided.Resolve<OptionalConsumer>().Greeter, Is.SameAs(greeter));
        }

        private sealed class AsyncStarter : IAsyncStartableEntryPoint
        {
            public bool Started;
            public CancellationToken Token;
            public UniTask StartAsync(CancellationToken cancellationToken)
            {
                Started = true;
                Token = cancellationToken;
                return UniTask.CompletedTask;
            }
        }

        private sealed class OptionalConsumer
        {
            public IGreeter Greeter { get; }
            public OptionalConsumer(IGreeter greeter = null) => Greeter = greeter;
        }

        private interface IGreeter
        {
        }

        private sealed class Greeter : IGreeter, IDisposable
        {
            public int Disposed;

            public void Dispose() => Disposed++;
        }

        private sealed class Consumer
        {
            public Consumer(IGreeter greeter) => Greeter = greeter;

            public IGreeter Greeter { get; }
        }

        private sealed class Starter : IStartableEntryPoint, ITickableEntryPoint
        {
            public int Started;
            public int Ticked;

            public void Start() => Started++;

            public void Tick() => Ticked++;
        }

        [Test]
        public void ConstructorArguments_AreResolvedFromTheScope()
        {
            using var scope = MinimalServiceScope.Create(registry =>
            {
                registry.Register<Greeter>(ServiceLifetime.Singleton).As(typeof(IGreeter));
                registry.Register<Consumer>(ServiceLifetime.Transient);
            });

            Assert.That(scope.Resolve(typeof(Consumer)), Is.Not.SameAs(scope.Resolve(typeof(Consumer))));
            Assert.That(((Consumer)scope.Resolve(typeof(Consumer))).Greeter, Is.SameAs(scope.Resolve(typeof(IGreeter))));
        }

        [Test]
        public void ChildScope_ShadowsItsParent_AndSingletonsStayWithTheirOwner()
        {
            using var parent = MinimalServiceScope.Create(registry =>
                registry.Register<Greeter>(ServiceLifetime.Singleton).As(typeof(IGreeter)));
            var parentGreeter = (Greeter)parent.Resolve(typeof(IGreeter));
            var child = parent.CreateScope(registry =>
                registry.Register<Greeter>(ServiceLifetime.Singleton).As(typeof(IGreeter)));
            var childGreeter = (Greeter)child.Resolve(typeof(IGreeter));

            child.Dispose();

            Assert.That(childGreeter, Is.Not.SameAs(parentGreeter));
            Assert.That(childGreeter.Disposed, Is.EqualTo(1));
            Assert.That(parentGreeter.Disposed, Is.EqualTo(0));
        }

        [Test]
        public void ScopedService_IsOneInstancePerScope_EvenWhenRegisteredByTheParent()
        {
            using var parent = MinimalServiceScope.Create(registry =>
                registry.Register<Greeter>(ServiceLifetime.Scoped).As(typeof(IGreeter)));
            using var child = (MinimalServiceScope)parent.CreateScope();

            Assert.That(child.Resolve(typeof(IGreeter)), Is.SameAs(child.Resolve(typeof(IGreeter))));
            Assert.That(child.Resolve(typeof(IGreeter)), Is.Not.SameAs(parent.Resolve(typeof(IGreeter))));
        }

        [Test]
        public void EntryPoint_StartsOnceWhenTheScopeIsBuilt_AndTicksOnDemand()
        {
            var starter = new Starter();
            using var scope = MinimalServiceScope.Create(registry => registry.RegisterEntryPoint(_ => starter));

            scope.Tick();
            scope.Tick();

            Assert.That(starter.Started, Is.EqualTo(1));
            Assert.That(starter.Ticked, Is.EqualTo(2));
        }

        [Test]
        public void DisposeCallbacks_RunBeforeOwnedServicesAreDisposed()
        {
            Greeter greeter = null;
            var disposedWhenCallbackRan = -1;
            var scope = MinimalServiceScope.Create(registry =>
            {
                registry.Register<Greeter>(ServiceLifetime.Singleton).As(typeof(IGreeter));
                registry.RegisterDisposeCallback(resolver =>
                {
                    greeter = (Greeter)resolver.Resolve(typeof(IGreeter));
                    disposedWhenCallbackRan = greeter.Disposed;
                });
            });

            scope.Dispose();

            Assert.That(disposedWhenCallbackRan, Is.EqualTo(0));
            Assert.That(greeter.Disposed, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_AttemptsEveryCallbackAndOwnedService_WhenSomeThrow()
        {
            var laterService = new TrackingDisposable();
            var laterCallbackRan = false;
            var scope = MinimalServiceScope.Create(registry =>
            {
                registry.Register<ThrowingDisposable>(ServiceLifetime.Singleton);
                registry.Register(_ => laterService, ServiceLifetime.Singleton);
                registry.RegisterDisposeCallback(_ => throw new InvalidOperationException("callback failed"));
                registry.RegisterDisposeCallback(_ => laterCallbackRan = true);
            });
            scope.Resolve<ThrowingDisposable>();
            scope.Resolve<TrackingDisposable>();

            Assert.Throws<AggregateException>(scope.Dispose);

            Assert.That(laterCallbackRan, Is.True);
            Assert.That(laterService.Disposed, Is.EqualTo(1));
            Assert.That(scope.IsDisposed, Is.True);
        }

        [Test]
        public void RootBuildFailure_DisposesCreatedServicesAndCancelsStartedEntryPoints()
        {
            var starter = new PendingAsyncStarter();
            TrackingDisposable tracked = null;
            var disposeCallbackRan = false;

            var exception = Assert.Throws<InvalidOperationException>(() => MinimalServiceScope.Create(registry =>
            {
                registry.Register<TrackingDisposable>(ServiceLifetime.Singleton);
                registry.RegisterBuildCallback(resolver =>
                    tracked = (TrackingDisposable)resolver.Resolve(typeof(TrackingDisposable)));
                registry.RegisterDisposeCallback(_ => disposeCallbackRan = true);
                registry.RegisterEntryPoint(_ => starter);
                registry.RegisterEntryPoint(_ => new ThrowingStarter());
            }));

            Assert.That(exception.Message, Is.EqualTo("start failed"));
            Assert.That(tracked.Disposed, Is.EqualTo(1));
            Assert.That(disposeCallbackRan, Is.True);
            Assert.That(starter.Token.IsCancellationRequested, Is.True);
        }

        [Test]
        public void ChildBuildFailure_DisposesOnlyTheFailedChild()
        {
            using var parent = MinimalServiceScope.Create(registry =>
                registry.Register<TrackingDisposable>(ServiceLifetime.Singleton));
            var parentService = parent.Resolve<TrackingDisposable>();
            TrackingDisposable childService = null;

            Assert.Throws<InvalidOperationException>(() => parent.CreateScope(registry =>
            {
                registry.Register<TrackingDisposable>(ServiceLifetime.Singleton);
                registry.RegisterBuildCallback(resolver =>
                    childService = (TrackingDisposable)resolver.Resolve(typeof(TrackingDisposable)));
                registry.RegisterEntryPoint(_ => new ThrowingStarter());
            }));

            Assert.That(childService.Disposed, Is.EqualTo(1));
            Assert.That(parentService.Disposed, Is.Zero);
            Assert.That(parent.IsDisposed, Is.False);
        }

        private sealed class ThrowingDisposable : IDisposable
        {
            public ThrowingDisposable()
            {
            }

            public void Dispose() => throw new InvalidOperationException("dispose failed");
        }

        private sealed class TrackingDisposable : IDisposable
        {
            public int Disposed;
            public void Dispose() => Disposed++;
        }

        private sealed class PendingAsyncStarter : IAsyncStartableEntryPoint
        {
            private readonly UniTaskCompletionSource _completion = new();

            public CancellationToken Token;

            public async UniTask StartAsync(CancellationToken cancellationToken)
            {
                Token = cancellationToken;
                await _completion.Task.AttachExternalCancellation(cancellationToken);
            }
        }

        private sealed class ThrowingStarter : IStartableEntryPoint
        {
            public void Start() => throw new InvalidOperationException("start failed");
        }
    }
}
