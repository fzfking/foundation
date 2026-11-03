using System;
using System.Collections.Generic;

namespace Foundation.Requirements
{
    public static class Requirement
    {
        public static IRequirement Always { get; } = new AlwaysRequirement();

        public static IRequirement All(params IRequirement[] requirements) => new AllRequirement(requirements);

        public static IRequirement All(IEnumerable<IRequirement> requirements) => new AllRequirement(requirements);

        public static IRequirement Any(params IRequirement[] requirements) => new AnyRequirement(requirements);

        public static IRequirement Any(IEnumerable<IRequirement> requirements) => new AnyRequirement(requirements);

        public static IRequirement Not(
            IRequirement requirement,
            string failureCode,
            IReadOnlyDictionary<string, object> arguments = null)
            => new NotRequirement(requirement, failureCode, arguments);

        public static IRequirement FromCondition(
            ICondition condition,
            string failureCode,
            IReadOnlyDictionary<string, object> arguments = null)
            => new ConditionRequirement(condition, failureCode, arguments);

        public static IRequirement FromPredicate(
            Func<bool> predicate,
            string failureCode,
            IReadOnlyDictionary<string, object> arguments = null)
            => new ConditionRequirement(new PredicateCondition(predicate), failureCode, arguments);

        private sealed class AlwaysRequirement : IRequirement
        {
            public RequirementResult Evaluate() => RequirementResult.Satisfied;
        }

        private sealed class PredicateCondition : ICondition
        {
            private readonly Func<bool> _predicate;

            public PredicateCondition(Func<bool> predicate)
            {
                _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            }

            public bool IsSatisfied() => _predicate();
        }
    }

    public sealed class ConditionRequirement : IRequirement
    {
        private readonly ICondition _condition;
        private readonly RequirementResult _failed;

        public ConditionRequirement(
            ICondition condition,
            string failureCode,
            IReadOnlyDictionary<string, object> arguments = null)
        {
            _condition = condition ?? throw new ArgumentNullException(nameof(condition));
            if (string.IsNullOrWhiteSpace(failureCode))
                throw new ArgumentException("Requirement failure code is required.", nameof(failureCode));

            // Results are immutable, so a gate checked every frame or event does not allocate while it stays closed.
            _failed = RequirementResult.Failed(failureCode, arguments);
        }

        public RequirementResult Evaluate() => _condition.IsSatisfied() ? RequirementResult.Satisfied : _failed;
    }

    public sealed class AllRequirement : IRequirement
    {
        private readonly IRequirement[] _requirements;

        public AllRequirement(IEnumerable<IRequirement> requirements)
        {
            _requirements = RequirementArray.Create(requirements, nameof(requirements));
        }

        public RequirementResult Evaluate()
        {
            List<RequirementFailure> failures = null;
            for (var index = 0; index < _requirements.Length; index++)
            {
                var result = _requirements[index].Evaluate();
                if (result.IsSatisfied)
                    continue;

                failures ??= new List<RequirementFailure>();
                failures.AddRange(result.Failures);
            }

            return failures == null ? RequirementResult.Satisfied : RequirementResult.Failed(failures);
        }
    }

    public sealed class AnyRequirement : IRequirement
    {
        private readonly IRequirement[] _requirements;

        public AnyRequirement(IEnumerable<IRequirement> requirements)
        {
            _requirements = RequirementArray.Create(requirements, nameof(requirements));
            if (_requirements.Length == 0)
                throw new ArgumentException("Any requires at least one requirement.", nameof(requirements));
        }

        public RequirementResult Evaluate()
        {
            var failures = new List<RequirementFailure>();
            foreach (var requirement in _requirements)
            {
                var result = requirement.Evaluate();
                if (result.IsSatisfied)
                    return RequirementResult.Satisfied;

                failures.AddRange(result.Failures);
            }

            return RequirementResult.Failed(failures);
        }
    }

    public sealed class NotRequirement : IRequirement
    {
        private readonly IRequirement _requirement;
        private readonly string _failureCode;
        private readonly IReadOnlyDictionary<string, object> _arguments;

        public NotRequirement(
            IRequirement requirement,
            string failureCode,
            IReadOnlyDictionary<string, object> arguments = null)
        {
            _requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
            if (string.IsNullOrWhiteSpace(failureCode))
                throw new ArgumentException("Requirement failure code is required.", nameof(failureCode));

            _failureCode = failureCode;
            _arguments = arguments;
        }

        public RequirementResult Evaluate() => _requirement.Evaluate().IsSatisfied
            ? RequirementResult.Failed(_failureCode, _arguments)
            : RequirementResult.Satisfied;
    }

    internal static class RequirementArray
    {
        public static IRequirement[] Create(IEnumerable<IRequirement> requirements, string parameterName)
        {
            if (requirements == null)
                throw new ArgumentNullException(parameterName);

            var list = new List<IRequirement>();
            foreach (var requirement in requirements)
            {
                if (requirement == null)
                    throw new ArgumentException("Requirement composition does not accept null children.", parameterName);

                list.Add(requirement);
            }

            return list.ToArray();
        }
    }
}
