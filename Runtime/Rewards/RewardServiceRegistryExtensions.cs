using System;
using Foundation.DI;

namespace Foundation.Rewards
{
    public static class RewardServiceRegistryExtensions
    {
        /// <summary><see cref="RewardService"/> as <see cref="IRewardService"/> (and as itself, for handler registration).</summary>
        public static IServiceRegistry UseRewards(this IServiceRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            registry.Register<RewardService>(ServiceLifetime.Singleton).As<IRewardService>().AsSelf();
            return registry;
        }

        /// <summary>Makes <typeparamref name="THandler"/> the handler of <typeparamref name="TReward"/> once the scope is built.</summary>
        public static IServiceRegistry UseRewardHandler<TReward, THandler>(this IServiceRegistry registry)
            where TReward : IReward
            where THandler : class, IRewardHandler<TReward>
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            registry.Register<THandler>(ServiceLifetime.Singleton);
            registry.RegisterBuildCallback(resolver =>
                resolver.Resolve<RewardService>().RegisterHandler<TReward>(resolver.Resolve<THandler>()));
            return registry;
        }
    }
}
