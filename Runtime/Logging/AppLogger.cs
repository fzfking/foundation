using System.Collections.Generic;

namespace Foundation.Logging
{
    public interface IAppLogger
    {
        void Log(LogLevel level, string category, string message);
    }

    public class AppLogger : IAppLogger
    {
        private readonly IReadOnlyList<ILogOutput> _outputs;

        public AppLogger(IEnumerable<ILogOutput> outputs)
        {
            _outputs = new List<ILogOutput>(outputs);
        }

        public void Log(LogLevel level, string category, string message)
        {
            foreach (var outputChannel in _outputs)
                outputChannel.Write(level, category, message);
        }
    }
}
