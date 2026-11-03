namespace Foundation.DI
{
    /// <summary>A feature's registrations, so a composition root applies a list of installers instead of one monolith.</summary>
    public interface IFeatureInstaller
    {
        void Install(IServiceRegistry registry);
    }
}
