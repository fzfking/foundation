using System;

namespace Foundation.Build
{
    public interface IBuildContext
    {
        string Version { get; }

        string BuildNumber { get; }

        string CommitHash { get; }

        DateTime? BuildDateUtc { get; }

        string Platform { get; }

        bool IsDevelopmentBuild { get; }
    }
}
