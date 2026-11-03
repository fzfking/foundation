namespace Foundation.Release
{
    /// <summary>Decides which release of an environment a client version may use, from the server's configuration.</summary>
    public interface IReleaseManager
    {
        ReleaseSelection Select(string environmentId, ClientVersion clientVersion, ReleaseConfig config);
    }
}
