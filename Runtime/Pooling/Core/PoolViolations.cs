using System;
using UnityEngine;

namespace Foundation.Pooling
{
    public enum PoolViolationKind
    {
        DoubleReturn,
        ForeignInstance,
        UseAfterDispose,
        LeakedOnDispose,
    }

    public interface IPoolViolationHandler
    {
        void OnViolation(IPool pool, PoolViolationKind kind, string detail);
    }

    public sealed class ThrowingPoolViolationHandler : IPoolViolationHandler
    {
        public static readonly ThrowingPoolViolationHandler Instance = new();

        public void OnViolation(IPool pool, PoolViolationKind kind, string detail)
            => throw new PoolViolationException(pool, kind, detail);
    }

    public sealed class LoggingPoolViolationHandler : IPoolViolationHandler
    {
        public static readonly LoggingPoolViolationHandler Instance = new();

        public void OnViolation(IPool pool, PoolViolationKind kind, string detail)
            => Debug.LogError($"[Pool '{pool?.Name}'] {kind}: {detail}");
    }

    public sealed class PoolViolationException : InvalidOperationException
    {
        public PoolViolationKind Kind { get; }

        public PoolViolationException(IPool pool, PoolViolationKind kind, string detail)
            : base($"[Pool '{pool?.Name}'] {kind}: {detail}")
        {
            Kind = kind;
        }
    }

    public sealed class PoolExhaustedException : InvalidOperationException
    {
        public PoolExhaustedException(string poolName, int maxActive)
            : base($"Pool '{poolName}' reached its limit of {maxActive} active instances.")
        {
        }
    }

    /// <summary>
    /// Defaults for violation checking. Checks cost a hash lookup per rent/return, so they are on only in the
    /// Editor and development builds unless a pool says otherwise.
    /// </summary>
    public static class PoolDefaults
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public const bool SafetyChecks = true;
#else
        public const bool SafetyChecks = false;
#endif

        public static IPoolViolationHandler ViolationHandler { get; set; } = LoggingPoolViolationHandler.Instance;
    }
}
