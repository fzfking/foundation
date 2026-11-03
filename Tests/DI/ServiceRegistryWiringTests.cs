using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.DI;
using Foundation.ErrorHandling;
using Foundation.FeatureFlags;
using Foundation.Logging;
using Foundation.Messaging;
using Foundation.Rewards;
using Foundation.Scheduling;
using Foundation.StateMachine;
using Foundation.Timing;
using NUnit.Framework;

namespace Foundation.Tests
{
    public sealed class ServiceRegistryWiringTests
    {
        [Test]
        public void TheUseExtensions_ComposeTheServicesThatDependOnEachOther()
        {
            // ErrorHandler needs a logger and the event bus; the scheduler needs a clock. A wiring that forgets
            // one of those only fails when something resolves it, so resolve every service of the stack.
            using var scope = MinimalServiceScope.Create(registry => registry
                .UseTime()
                .UseLogging()
                .UseMessaging()
                .UseErrorHandling()
                .UseScheduler());

            Assert.That(scope.Resolve<ITimeProvider>(), Is.Not.Null);
            Assert.That(scope.Resolve<IAppLogger>(), Is.Not.Null);
            Assert.That(scope.Resolve<ICommandBus>(), Is.Not.Null);
            Assert.That(scope.Resolve<IEventBus>(), Is.Not.Null);
            Assert.That(scope.Resolve<IErrorHandler>(), Is.Not.Null);
            Assert.That(scope.Resolve<IScheduler>(), Is.Not.Null);
        }

        [Test]
        public void UseRewardHandler_ConnectsTheHandlerOnceTheScopeIsBuilt()
        {
            using var scope = MinimalServiceScope.Create(registry => registry
                .UseRewards()
                .UseRewardHandler<GemReward, GemRewardHandler>());

            var result = scope.Resolve<IRewardService>()
                .GrantAsync(new RewardBundle(new IReward[] { new GemReward(3) }))
                .GetAwaiter().GetResult();

            Assert.That(result.IsFullyGranted, Is.True);
            Assert.That(scope.Resolve<GemRewardHandler>().Granted, Is.EqualTo(3));
        }

        [Test]
        public void UseStateMachine_EntersStatesThatTheScopeProvides()
        {
            using var scope = MinimalServiceScope.Create(registry =>
            {
                registry.UseStateMachine<IMenuState>();
                registry.Register<MainMenuState>(ServiceLifetime.Singleton);
            });

            scope.Resolve<IStateMachine<IMenuState>>().EnterAsync<MainMenuState>().GetAwaiter().GetResult();

            Assert.That(scope.Resolve<MainMenuState>().Entered, Is.EqualTo(1));
        }

        [Test]
        public void UseFeatureFlags_AnswersFromTheGivenSource()
        {
            using var scope = MinimalServiceScope.Create(registry =>
                registry.UseFeatureFlags(new FixedFlagSource(on: "moon", off: "sun")));
            var flags = scope.Resolve<IFeatureFlagService>();

            Assert.That(flags.IsEnabledAsync("moon").GetAwaiter().GetResult(), Is.True);
            Assert.That(flags.IsEnabledAsync("sun").GetAwaiter().GetResult(), Is.False);
        }

        [Test]
        public void AContextScheduler_AnswersBothCapabilitiesWithOneWrapper_AndCancelsAllTimersWithTheScope()
        {
            var time = new ManualTimeProvider();
            using var application = MinimalServiceScope.Create(registry =>
            {
                registry.RegisterInstance<ITimeProvider>(time);
                registry.RegisterScheduler();
            });

            var applicationScheduler = application.Resolve<Scheduler>();
            Assert.That(application.Resolve<IScheduler>(), Is.SameAs(applicationScheduler));
            Assert.That(application.Resolve<IFixedRateScheduler>(), Is.SameAs(applicationScheduler),
                "the entry point is exported as every interface it implements");

            var onceFired = 0;
            var rateFired = 0;
            using (var context = application.CreateScope(registry => registry.RegisterScopedScheduler()))
            {
                var scoped = context.Resolve<IScheduler>();
                var scopedRate = context.Resolve<IFixedRateScheduler>();

                Assert.That(scoped, Is.TypeOf<ScopedScheduler>());
                Assert.That(scopedRate, Is.SameAs(scoped), "both capabilities must resolve to the same scoped wrapper");

                scoped.ScheduleOnce(0.5f, () => onceFired++);
                scopedRate.ScheduleFixedRateRepeating(0.1f, () => rateFired++, 5, FixedRateBacklogPolicy.Preserve);

                time.UnscaledDeltaTime = 1f;
                applicationScheduler.Tick();
                Assert.That(onceFired, Is.EqualTo(1));
                Assert.That(rateFired, Is.EqualTo(5));
            }

            time.UnscaledDeltaTime = 0f;
            applicationScheduler.Tick();
            Assert.That(onceFired, Is.EqualTo(1));
            Assert.That(rateFired, Is.EqualTo(5), "a timer from either capability leaves with the scope");
        }

        private sealed class ManualTimeProvider : ITimeProvider
        {
            public DateTime UtcNow { get; set; } = DateTime.UtcNow;

            public float Time { get; set; }

            public float DeltaTime { get; set; }

            public float UnscaledDeltaTime { get; set; }
        }

        private interface IMenuState : IState
        {
        }

        private sealed class MainMenuState : IMenuState
        {
            public int Entered { get; private set; }

            public UniTask EnterAsync()
            {
                Entered++;
                return UniTask.CompletedTask;
            }

            public void Update()
            {
            }

            public UniTask ExitAsync() => UniTask.CompletedTask;
        }

        private sealed class GemReward : IReward
        {
            public GemReward(int amount) => Amount = amount;

            public int Amount { get; }
        }

        private sealed class GemRewardHandler : IRewardHandler<GemReward>
        {
            public int Granted { get; private set; }

            public UniTask<RewardGrantResult> GrantAsync(GemReward reward, CancellationToken cancellationToken = default)
            {
                Granted += reward.Amount;
                return UniTask.FromResult(RewardGrantResult.Granted(reward));
            }
        }

        private sealed class FixedFlagSource : IFeatureFlagSource
        {
            private readonly string _on;
            private readonly string _off;

            public FixedFlagSource(string on, string off)
            {
                _on = on;
                _off = off;
            }

            public UniTask<bool?> TryGetAsync(string featureId, CancellationToken cancellationToken = default)
                => UniTask.FromResult<bool?>(featureId == _on ? true : featureId == _off ? false : null);
        }
    }
}
