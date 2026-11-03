using UnityEngine;

namespace Foundation.Logging
{
    public interface ILogOutput
    {
        void Write(LogLevel level, string category, string message);
    }

    public class UnityLogOutput : ILogOutput
    {
        public void Write(LogLevel level, string category, string message)
        {
            var formatted = $"[{category}] {message}";

            switch (level)
            {
                case LogLevel.Debug:
                case LogLevel.Info:
                    Debug.Log(formatted);
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning(formatted);
                    break;
                case LogLevel.Error:
                    Debug.LogError(formatted);
                    break;
            }
        }
    }
}
