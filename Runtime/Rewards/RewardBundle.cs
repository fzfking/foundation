using System;
using System.Collections;
using System.Collections.Generic;

namespace Foundation.Rewards
{
    public sealed class RewardBundle : IReadOnlyList<IReward>
    {
        public static readonly RewardBundle Empty = new(Array.Empty<IReward>());

        private readonly IReward[] _rewards;

        public int Count => _rewards.Length;

        public IReward this[int index] => _rewards[index];

        public RewardBundle(IEnumerable<IReward> rewards)
        {
            if (rewards == null)
                throw new ArgumentNullException(nameof(rewards));

            var list = new List<IReward>();
            foreach (var reward in rewards)
            {
                if (reward == null)
                    throw new ArgumentException("A reward bundle does not accept null rewards.", nameof(rewards));

                list.Add(reward);
            }

            _rewards = list.ToArray();
        }

        public RewardBundle(params IReward[] rewards)
            : this((IEnumerable<IReward>)(rewards ?? Array.Empty<IReward>()))
        {
        }

        public IEnumerator<IReward> GetEnumerator() => ((IEnumerable<IReward>)_rewards).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _rewards.GetEnumerator();
    }
}
