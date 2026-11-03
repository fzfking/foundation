namespace Foundation.Build
{
    public enum GameEnvironment
    {
        Development,
        Staging,
        Production
    }

    public interface IEnvironmentContext
    {
        GameEnvironment Environment { get; }
    }

    public static class EnvironmentContextExtensions
    {
        public static bool IsDevelopment(this IEnvironmentContext context)
            => context != null && context.Environment == GameEnvironment.Development;

        public static bool IsProduction(this IEnvironmentContext context)
            => context != null && context.Environment == GameEnvironment.Production;
    }

    public sealed class EnvironmentContext : IEnvironmentContext
    {
        public GameEnvironment Environment { get; }

        public EnvironmentContext(GameEnvironment environment) => Environment = environment;

        public override string ToString() => Environment.ToString();
    }
}
