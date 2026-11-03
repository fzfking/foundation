using System;

namespace Foundation.Tutorial.Flow
{
    /// <summary>
    /// One compiled tutorial chapter: header plus steps, each a tree of primitive commands. Produced from the
    /// chapter's DSL file by <see cref="TutorialScriptParser"/> at build time and shipped as config data.
    /// </summary>
    [Serializable]
    public sealed class TutorialScriptConfig
    {
        public string Id;
        public string TriggerId;
        public int Priority;
        public TutorialStepScript[] Steps;
    }

    [Serializable]
    public sealed class TutorialStepScript
    {
        public string Id;
        /// <summary>A resumed chapter restarts from the step after the last completed checkpoint.</summary>
        public bool IsCheckpoint;
        public TutorialNode[] Body;
        /// <summary>Runs after <see cref="Body"/> however the step ends, including cancellation.</summary>
        public TutorialNode[] Finally;
    }

    public enum TutorialNodeKind : byte
    {
        Command,
        If,
        Race,
    }

    /// <summary>
    /// A command (<c>say</c>, <c>wait</c>, <c>action</c>...) or a block. <see cref="Command"/> and
    /// <see cref="Arguments"/> hold the name and positional arguments, or the condition of an <c>if</c>;
    /// <see cref="Children"/> and <see cref="ElseChildren"/> the nested nodes.
    /// </summary>
    [Serializable]
    public sealed class TutorialNode
    {
        public TutorialNodeKind Kind;
        public string Command;
        public string[] Arguments;
        public TutorialNamedArgument[] NamedArguments;

        /// <summary>For an <c>if</c> block: whether the condition is negated (<c>if not ...</c>).</summary>
        public bool Negated;

        public TutorialNode[] Children;
        public TutorialNode[] ElseChildren;

        public int Line;

        public string GetNamed(string name, string fallback = null)
        {
            if (NamedArguments == null)
                return fallback;

            foreach (var argument in NamedArguments)
            {
                if (argument != null && string.Equals(argument.Name, name, StringComparison.Ordinal))
                    return argument.Value;
            }

            return fallback;
        }
    }

    [Serializable]
    public sealed class TutorialNamedArgument
    {
        public string Name;
        public string Value;
    }
}
