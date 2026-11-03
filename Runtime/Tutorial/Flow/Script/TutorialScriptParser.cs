using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Foundation.Tutorial.Flow
{
    public readonly struct TutorialScriptError
    {
        public int Line { get; }
        public string Message { get; }

        public TutorialScriptError(int line, string message)
        {
            Line = line;
            Message = message;
        }

        public override string ToString() => Line > 0 ? $"line {Line}: {Message}" : Message;
    }

    /// <summary>
    /// Parses a chapter script. Syntax only: which commands exist and what their arguments mean is checked by
    /// <see cref="TutorialScriptValidator"/>.
    /// <code>
    /// chapter tutorial.chapter1
    /// trigger empire-screen-ready
    /// priority 0
    /// checkpoints all
    /// 
    /// step tutorial.chapter1.step1-buy-first-production
    ///   skip_if production.owned_at_least production.night-hunt 1
    ///   highlight production.night-hunt.buy
    ///   say tutorial.step1.dialogue continue=none
    ///   wait production.purchased production.night-hunt
    ///   finally
    ///     action production.speed_boost off
    /// end
    /// </code>
    /// Blocks: <c>if [not] &lt;condition&gt; args ... [else ...] end</c> and <c>race ... end</c>, where every
    /// direct child of a race is a branch and the first to finish wins.
    /// </summary>
    public static class TutorialScriptParser
    {
        public const string ChapterKeyword = "chapter";
        public const string TriggerKeyword = "trigger";
        public const string PriorityKeyword = "priority";
        public const string CheckpointsKeyword = "checkpoints";
        public const string StepKeyword = "step";
        public const string CheckpointFlag = "checkpoint";
        public const string EndKeyword = "end";
        public const string IfKeyword = "if";
        public const string ElseKeyword = "else";
        public const string RaceKeyword = "race";
        public const string FinallyKeyword = "finally";
        public const string NotKeyword = "not";

        public static TutorialScriptConfig Parse(string text, List<TutorialScriptError> errors)
        {
            if (errors == null)
                throw new ArgumentNullException(nameof(errors));

            var parser = new State(errors);
            var lines = (text ?? string.Empty).Split('\n');
            for (var index = 0; index < lines.Length; index++)
                parser.ParseLine(index + 1, lines[index]);

            return parser.Finish(lines.Length);
        }

        /// <summary>Splits a line into tokens: whitespace separated, double quotes group, <c>#</c> starts a comment.</summary>
        public static List<string> Tokenize(string line, int lineNumber, List<TutorialScriptError> errors)
        {
            var tokens = new List<string>();
            var builder = new StringBuilder();
            var quoted = false;
            var hasToken = false;
            foreach (var character in line)
            {
                if (character == '"')
                {
                    quoted = !quoted;
                    hasToken = true;
                    continue;
                }

                if (!quoted && character == '#')
                    break;

                if (!quoted && char.IsWhiteSpace(character))
                {
                    if (hasToken)
                    {
                        tokens.Add(builder.ToString());
                        builder.Clear();
                        hasToken = false;
                    }

                    continue;
                }

                builder.Append(character);
                hasToken = true;
            }

            if (quoted)
                errors.Add(new TutorialScriptError(lineNumber, "Unterminated quoted value."));
            if (hasToken)
                tokens.Add(builder.ToString());
            return tokens;
        }

        private sealed class State
        {
            private readonly List<TutorialScriptError> _errors;
            private readonly List<TutorialStepScript> _steps = new();
            private readonly Stack<Frame> _frames = new();
            private readonly HashSet<string> _stepIds = new(StringComparer.Ordinal);

            private string _chapterId;
            private string _triggerId;
            private int _priority;
            private bool _allCheckpoints;
            private TutorialStepScript _currentStep;
            private int _currentStepLine;

            public State(List<TutorialScriptError> errors)
            {
                _errors = errors;
            }

            public void ParseLine(int lineNumber, string rawLine)
            {
                var tokens = Tokenize(rawLine.TrimEnd('\r'), lineNumber, _errors);
                if (tokens.Count == 0)
                    return;

                if (_currentStep == null)
                    ParseHeaderLine(lineNumber, tokens);
                else
                    ParseStepLine(lineNumber, tokens);
            }

            public TutorialScriptConfig Finish(int lastLine)
            {
                if (_currentStep != null)
                    Error(_currentStepLine, $"Step '{_currentStep.Id}' is missing its closing 'end'.");
                if (string.IsNullOrWhiteSpace(_chapterId))
                    Error(0, "The script has no 'chapter <id>' line.");
                if (string.IsNullOrWhiteSpace(_triggerId))
                    Error(0, "The script has no 'trigger <id>' line; nothing could start this chapter.");
                if (_steps.Count == 0)
                    Error(lastLine, "The chapter has no steps.");

                if (_allCheckpoints)
                {
                    foreach (var step in _steps)
                        step.IsCheckpoint = true;
                }

                return new TutorialScriptConfig
                {
                    Id = _chapterId,
                    TriggerId = _triggerId,
                    Priority = _priority,
                    Steps = _steps.ToArray(),
                };
            }

            private void ParseHeaderLine(int line, List<string> tokens)
            {
                var keyword = tokens[0];
                switch (keyword)
                {
                    case ChapterKeyword:
                        if (ExpectCount(line, tokens, 2, "chapter <id>"))
                            SetOnce(ref _chapterId, tokens[1], line, ChapterKeyword);
                        return;
                    case TriggerKeyword:
                        if (ExpectCount(line, tokens, 2, "trigger <id>"))
                            SetOnce(ref _triggerId, tokens[1], line, TriggerKeyword);
                        return;
                    case PriorityKeyword:
                        if (!ExpectCount(line, tokens, 2, "priority <number>"))
                            return;
                        if (!int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _priority))
                            Error(line, $"Priority '{tokens[1]}' is not a whole number.");
                        return;
                    case CheckpointsKeyword:
                        if (!ExpectCount(line, tokens, 2, "checkpoints all|none"))
                            return;
                        if (tokens[1] == "all")
                            _allCheckpoints = true;
                        else if (tokens[1] == "none")
                            _allCheckpoints = false;
                        else
                            Error(line, $"Expected 'checkpoints all' or 'checkpoints none', got '{tokens[1]}'.");
                        return;
                    case StepKeyword:
                        BeginStep(line, tokens);
                        return;
                    default:
                        Error(line, $"'{keyword}' is not allowed outside a step. Expected chapter, trigger, priority, checkpoints or step.");
                        return;
                }
            }

            private void BeginStep(int line, List<string> tokens)
            {
                if (tokens.Count < 2 || tokens.Count > 3 || (tokens.Count == 3 && tokens[2] != CheckpointFlag))
                {
                    Error(line, "Expected 'step <id>' or 'step <id> checkpoint'.");
                    return;
                }

                var id = tokens[1];
                if (!_stepIds.Add(id))
                    Error(line, $"Step '{id}' is declared more than once.");

                _currentStep = new TutorialStepScript { Id = id, IsCheckpoint = tokens.Count == 3 };
                _currentStepLine = line;
                _frames.Clear();
                _frames.Push(new Frame(FrameKind.StepBody, line));
            }

            private void ParseStepLine(int line, List<string> tokens)
            {
                var keyword = tokens[0];
                var frame = _frames.Peek();
                switch (keyword)
                {
                    case EndKeyword:
                        ExpectCount(line, tokens, 1, "end");
                        CloseFrame();
                        return;
                    case ElseKeyword:
                        ExpectCount(line, tokens, 1, "else");
                        if (frame.Kind != FrameKind.If)
                        {
                            Error(line, "'else' is only allowed inside an 'if' block.");
                            return;
                        }

                        frame.Kind = FrameKind.Else;
                        return;
                    case FinallyKeyword:
                        ExpectCount(line, tokens, 1, "finally");
                        if (frame.Kind != FrameKind.StepBody || _frames.Count != 1)
                        {
                            Error(line, "'finally' is only allowed directly inside a step, once.");
                            return;
                        }

                        frame.Kind = FrameKind.StepFinally;
                        return;
                    case StepKeyword:
                    case ChapterKeyword:
                    case TriggerKeyword:
                    case PriorityKeyword:
                    case CheckpointsKeyword:
                        Error(line, $"'{keyword}' found inside step '{_currentStep.Id}'; is an 'end' missing?");
                        return;
                    case IfKeyword:
                        OpenIf(line, tokens);
                        return;
                    case RaceKeyword:
                        ExpectCount(line, tokens, 1, "race");
                        var race = new TutorialNode { Kind = TutorialNodeKind.Race, Line = line };
                        Add(race);
                        _frames.Push(new Frame(FrameKind.Race, line, race));
                        return;
                    default:
                        Add(CreateCommand(line, tokens));
                        return;
                }
            }

            private void OpenIf(int line, List<string> tokens)
            {
                var negated = tokens.Count > 1 && tokens[1] == NotKeyword;
                var conditionIndex = negated ? 2 : 1;
                if (tokens.Count <= conditionIndex)
                {
                    Error(line, "Expected 'if [not] <condition> args...'.");
                    return;
                }

                var node = new TutorialNode
                {
                    Kind = TutorialNodeKind.If,
                    Command = tokens[conditionIndex],
                    Arguments = tokens.GetRange(conditionIndex + 1, tokens.Count - conditionIndex - 1).ToArray(),
                    Negated = negated,
                    Line = line,
                };
                Add(node);
                _frames.Push(new Frame(FrameKind.If, line, node));
            }

            private TutorialNode CreateCommand(int line, List<string> tokens)
            {
                var positional = new List<string>();
                var named = new List<TutorialNamedArgument>();
                for (var index = 1; index < tokens.Count; index++)
                {
                    var token = tokens[index];
                    var separator = token.IndexOf('=');
                    if (separator > 0 && IsIdentifier(token, separator))
                    {
                        var name = token.Substring(0, separator);
                        if (named.Exists(argument => argument.Name == name))
                            Error(line, $"Named argument '{name}' is given more than once.");
                        named.Add(new TutorialNamedArgument { Name = name, Value = token.Substring(separator + 1) });
                        continue;
                    }

                    if (named.Count > 0)
                        Error(line, $"Positional argument '{token}' follows named arguments.");
                    positional.Add(token);
                }

                return new TutorialNode
                {
                    Kind = TutorialNodeKind.Command,
                    Command = tokens[0],
                    Arguments = positional.ToArray(),
                    NamedArguments = named.ToArray(),
                    Line = line,
                };
            }

            private void Add(TutorialNode node)
            {
                if (node != null)
                    _frames.Peek().Nodes.Add(node);
            }

            private void CloseFrame()
            {
                var frame = _frames.Pop();
                switch (frame.Kind)
                {
                    case FrameKind.If:
                        frame.Node.Children = frame.Nodes.ToArray();
                        frame.Node.ElseChildren = Array.Empty<TutorialNode>();
                        return;
                    case FrameKind.Else:
                        frame.Node.Children = frame.ThenNodes.ToArray();
                        frame.Node.ElseChildren = frame.Nodes.ToArray();
                        return;
                    case FrameKind.Race:
                        if (frame.Nodes.Count < 2)
                            Error(frame.Line, "A 'race' needs at least two branches.");
                        frame.Node.Children = frame.Nodes.ToArray();
                        return;
                    case FrameKind.StepBody:
                        _currentStep.Body = frame.Nodes.ToArray();
                        _currentStep.Finally ??= Array.Empty<TutorialNode>();
                        FinishStep();
                        return;
                    case FrameKind.StepFinally:
                        _currentStep.Body = frame.ThenNodes.ToArray();
                        _currentStep.Finally = frame.Nodes.ToArray();
                        FinishStep();
                        return;
                }
            }

            private void FinishStep()
            {
                if (_currentStep.Body.Length == 0)
                    Error(_currentStepLine, $"Step '{_currentStep.Id}' has no commands.");
                _steps.Add(_currentStep);
                _currentStep = null;
            }

            private bool ExpectCount(int line, List<string> tokens, int count, string usage)
            {
                if (tokens.Count == count)
                    return true;

                Error(line, $"Expected '{usage}'.");
                return false;
            }

            private void SetOnce(ref string field, string value, int line, string keyword)
            {
                if (field != null)
                    Error(line, $"'{keyword}' is declared more than once.");
                field = value;
            }

            private void Error(int line, string message) => _errors.Add(new TutorialScriptError(line, message));

            private static bool IsIdentifier(string token, int length)
            {
                for (var index = 0; index < length; index++)
                {
                    var character = token[index];
                    if (!char.IsLetterOrDigit(character) && character != '_')
                        return false;
                }

                return true;
            }
        }

        private enum FrameKind
        {
            StepBody,
            StepFinally,
            If,
            Else,
            Race,
        }

        private sealed class Frame
        {
            private FrameKind _kind;

            public int Line { get; }
            public TutorialNode Node { get; }
            public List<TutorialNode> Nodes { get; private set; } = new();

            public List<TutorialNode> ThenNodes { get; private set; } = new();

            public FrameKind Kind
            {
                get => _kind;
                set
                {
                    ThenNodes = Nodes;
                    Nodes = new();
                    _kind = value;
                }
            }

            public Frame(FrameKind kind, int line, TutorialNode node = null)
            {
                _kind = kind;
                Line = line;
                Node = node;
            }
        }
    }
}
