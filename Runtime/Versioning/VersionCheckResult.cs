namespace Foundation.Versioning
{
    public sealed class VersionCheckResult
    {
        public string CurrentVersion { get; }
        public string LatestVersion { get; }
        public bool IsUpToDate { get; }

        public VersionCheckResult(string currentVersion, string latestVersion, bool isUpToDate)
        { CurrentVersion = currentVersion; LatestVersion = latestVersion; IsUpToDate = isUpToDate; }
    }
}
