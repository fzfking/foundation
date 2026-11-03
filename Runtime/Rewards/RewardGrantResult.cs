using System;
using System.Collections.Generic;

namespace Foundation.Rewards
{
    public enum RewardGrantStatus
    {
        Granted,
        Failed,
        Skipped
    }

    public sealed class RewardGrantResult
    {
        private static readonly IReadOnlyDictionary<string, object> NoDetails =
            new Dictionary<string, object>(0);

        public IReward Reward { get; }

        public RewardGrantStatus Status { get; }

        public bool IsGranted => Status == RewardGrantStatus.Granted;

        public string FailureCode { get; }

        public IReadOnlyDictionary<string, object> Details { get; }

        private RewardGrantResult(
            IReward reward,
            RewardGrantStatus status,
            string failureCode,
            IReadOnlyDictionary<string, object> details)
        {
            Reward = reward;
            Status = status;
            FailureCode = failureCode;
            Details = details ?? NoDetails;
        }

        public static RewardGrantResult Granted(
            IReward reward,
            IReadOnlyDictionary<string, object> details = null)
            => new(reward, RewardGrantStatus.Granted, null, details);

        public static RewardGrantResult Failed(
            IReward reward,
            string failureCode,
            IReadOnlyDictionary<string, object> details = null)
        {
            if (string.IsNullOrWhiteSpace(failureCode))
                throw new ArgumentException("A failed grant needs a failure code.", nameof(failureCode));

            return new RewardGrantResult(reward, RewardGrantStatus.Failed, failureCode, details);
        }

        public static RewardGrantResult Skipped(IReward reward)
            => new(reward, RewardGrantStatus.Skipped, null, null);
    }

    public sealed class RewardBundleResult
    {
        public IReadOnlyList<RewardGrantResult> Results { get; }

        public bool IsFullyGranted
        {
            get
            {
                foreach (var result in Results)
                {
                    if (!result.IsGranted)
                        return false;
                }

                return true;
            }
        }

        public RewardGrantResult FirstFailure
        {
            get
            {
                foreach (var result in Results)
                {
                    if (result.Status == RewardGrantStatus.Failed)
                        return result;
                }

                return null;
            }
        }

        public RewardBundleResult(IReadOnlyList<RewardGrantResult> results)
        {
            Results = results ?? Array.Empty<RewardGrantResult>();
        }
    }
}
