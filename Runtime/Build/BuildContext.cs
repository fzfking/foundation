using System;
using System.Text;

namespace Foundation.Build
{
    public sealed class BuildContext : IBuildContext
    {
        public string Version { get; }
        public string BuildNumber { get; }
        public string CommitHash { get; }
        public DateTime? BuildDateUtc { get; }
        public string Platform { get; }
        public bool IsDevelopmentBuild { get; }

        public BuildContext(
            string version,
            string platform,
            bool isDevelopmentBuild,
            string buildNumber = null,
            string commitHash = null,
            DateTime? buildDateUtc = null)
        {
            Version = string.IsNullOrWhiteSpace(version)
                ? throw new ArgumentException("Build version is required.", nameof(version))
                : version;
            Platform = string.IsNullOrWhiteSpace(platform)
                ? throw new ArgumentException("Build platform is required.", nameof(platform))
                : platform;
            IsDevelopmentBuild = isDevelopmentBuild;
            BuildNumber = Normalize(buildNumber);
            CommitHash = Normalize(commitHash);
            BuildDateUtc = buildDateUtc;
        }

        public override string ToString() => BuildContextText.Describe(this);

        private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static class BuildContextText
    {
        public static string Describe(IBuildContext build, IEnvironmentContext environment = null)
        {
            if (build == null)
                throw new ArgumentNullException(nameof(build));

            var builder = new StringBuilder();
            builder.Append("version=").Append(build.Version);
            if (build.BuildNumber != null)
                builder.Append(" build=").Append(build.BuildNumber);
            if (build.CommitHash != null)
                builder.Append(" commit=").Append(build.CommitHash);
            if (build.BuildDateUtc.HasValue)
                builder.Append(" built=").Append(build.BuildDateUtc.Value.ToString("O"));
            builder.Append(" platform=").Append(build.Platform);
            builder.Append(" development=").Append(build.IsDevelopmentBuild ? "true" : "false");
            if (environment != null)
                builder.Append(" environment=").Append(environment.Environment);
            return builder.ToString();
        }
    }
}
