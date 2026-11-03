using System;
using System.Collections.Generic;

namespace Foundation.Requirements
{
    public readonly struct RequirementFailure
    {
        private static readonly IReadOnlyDictionary<string, object> NoArguments =
            new Dictionary<string, object>(0);

        public string Code { get; }

        public IReadOnlyDictionary<string, object> Arguments { get; }

        public RequirementFailure(string code, IReadOnlyDictionary<string, object> arguments = null)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Requirement failure code is required.", nameof(code));

            Code = code;
            Arguments = arguments ?? NoArguments;
        }

        public override string ToString() => Code ?? string.Empty;
    }

    public sealed class RequirementResult
    {
        private static readonly IReadOnlyList<RequirementFailure> NoFailures =
            Array.Empty<RequirementFailure>();

        public static readonly RequirementResult Satisfied = new(NoFailures);

        public bool IsSatisfied => Failures.Count == 0;

        public IReadOnlyList<RequirementFailure> Failures { get; }

        public string FailureCode => Failures.Count > 0 ? Failures[0].Code : null;

        public IReadOnlyDictionary<string, object> FailureArguments =>
            Failures.Count > 0 ? Failures[0].Arguments : null;

        private RequirementResult(IReadOnlyList<RequirementFailure> failures)
        {
            Failures = failures ?? NoFailures;
        }

        public bool HasFailure(string code)
        {
            for (var index = 0; index < Failures.Count; index++)
            {
                if (string.Equals(Failures[index].Code, code, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        public static RequirementResult Failed(string code, IReadOnlyDictionary<string, object> arguments = null)
            => new(new[] { new RequirementFailure(code, arguments) });

        public static RequirementResult Failed(IReadOnlyList<RequirementFailure> failures)
        {
            if (failures == null || failures.Count == 0)
                throw new ArgumentException("A failed requirement result needs at least one failure.", nameof(failures));

            return new RequirementResult(failures);
        }
    }
}
