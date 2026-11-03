using System;

namespace Foundation.Content.Editor
{
    public static class ConfigBuildEntryPoint
    {
        public static Func<ConfigBuilder> BuilderFactory { get; set; } = CreateDefaultBuilder;

        private static ConfigBuilder CreateDefaultBuilder() => new(new ConfigBuildSettings(), new ConfigBuildRegistry());
    }
}
