using System.Collections.Generic;
using System.Linq;
using System;

namespace Foundation.Content.Editor
{
    public sealed class ConfigBuildReport
    {
        public bool Success { get; }

        public IReadOnlyList<ConfigBuildError> Errors { get; }

        public IReadOnlyList<string> WrittenFiles { get; }

        private ConfigBuildReport(
            bool success,
            IReadOnlyList<ConfigBuildError> errors,
            IReadOnlyList<string> writtenFiles)
        {
            Success = success;
            Errors = errors;
            WrittenFiles = writtenFiles;
        }

        public static ConfigBuildReport CreateFailure(IReadOnlyList<ConfigBuildError> errors)
            => new ConfigBuildReport(false, errors.ToArray(), Array.Empty<string>());

        public static ConfigBuildReport CreateSuccess(IReadOnlyList<string> writtenFiles)
            => new ConfigBuildReport(true, Array.Empty<ConfigBuildError>(), writtenFiles.ToArray());
    }
}
