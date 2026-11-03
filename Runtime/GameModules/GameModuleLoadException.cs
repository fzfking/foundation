using System;

namespace Foundation.GameModules
{
    public sealed class GameModuleLoadException : Exception
    {
        public GameModuleId ModuleId { get; }

        public GameModuleLoadException(GameModuleId moduleId, string message, Exception innerException = null)
            : base($"Failed to load game module '{moduleId}': {message}", innerException)
        {
            ModuleId = moduleId;
        }
    }
}
