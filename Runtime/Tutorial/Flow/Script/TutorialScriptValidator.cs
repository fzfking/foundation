using System;
using System.Collections.Generic;
using System.Globalization;

namespace Foundation.Tutorial.Flow
{
    public static class TutorialPrimitives
    {
        public const string Say = "say";
        public const string Highlight = "highlight";
        public const string ClearHighlight = "clear_highlight";
        public const string Show = "show";
        public const string Hide = "hide";
        public const string Close = "close";
        public const string Wait = "wait";
        public const string WaitUntil = "wait_until";
        public const string WaitTarget = "wait_target";
        public const string WaitClick = "wait_click";
        public const string Delay = "delay";
        public const string Action = "action";
        public const string SkipIf = "skip_if";

        public const string ContinueArgument = "continue";
        public const string TargetArgument = "target";
        public const string SideArgument = "side";
        public const string SpeakerArgument = "speaker";
        public const string ArrowArgument = "arrow";
        public const string PaddingArgument = "padding";
        public const string HandArgument = "hand";
        public const string TimeoutArgument = "timeout";

        public const string ContinueTap = "tap";
        public const string ContinueNone = "none";
    }

    /// <summary>
    /// Checks a parsed chapter against the built-in primitives and the game command catalog: unknown
    /// commands, wrong argument counts, invalid option values, unknown text keys, and a last step that never
    /// closes the tutorial overlay.
    /// </summary>
    public static class TutorialScriptValidator
    {
        private static readonly string[] Sides = { "auto", "above", "below", "left", "right" };
        private static readonly string[] ContinueModes = { TutorialPrimitives.ContinueTap, TutorialPrimitives.ContinueNone };
        private const int OverlayOpen = 1;
        private const int OverlayClosed = 2;

        public static void Validate(
            TutorialScriptConfig script,
            TutorialCommandCatalog catalog,
            Func<string, bool> textKeyExists,
            List<TutorialScriptError> errors)
        {
            if (script?.Steps == null)
                return;

            foreach (var step in script.Steps)
            {
                ValidateNodes(step.Body, catalog, textKeyExists, errors);
                ValidateNodes(step.Finally, catalog, textKeyExists, errors);
            }

            if (script.Steps.Length <= 0) 
                return;
            
            var last = script.Steps[^1];
            var body = AnalyzeNodes(last.Body, OverlayOpen);
            var cleanup = AnalyzeNodes(last.Finally, body.AllStates);
            if ((cleanup.AllStates & OverlayOpen) != 0)
                errors.Add(new TutorialScriptError(0,
                    $"The last step '{last.Id}' never runs 'close'; the tutorial overlay would stay open after the chapter."));
        }

        // NaN and infinity parse as numbers, but the runner throws on them, so they are rejected here.
        internal static bool TryParseNumber(string value, out float number)
            => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && float.IsFinite(number);

        private static void ValidateNodes(
            TutorialNode[] nodes,
            TutorialCommandCatalog catalog,
            Func<string, bool> textKeyExists,
            List<TutorialScriptError> errors)
        {
            if (nodes == null)
                return;

            foreach (var node in nodes)
            {
                switch (node.Kind)
                {
                    case TutorialNodeKind.If:
                        ValidateGameCommand(node.Line, TutorialCommandCategory.Condition, node.Command, node.Arguments, catalog, errors);
                        ValidateNodes(node.Children, catalog, textKeyExists, errors);
                        ValidateNodes(node.ElseChildren, catalog, textKeyExists, errors);
                        break;
                    case TutorialNodeKind.Race:
                        ValidateNodes(node.Children, catalog, textKeyExists, errors);
                        break;
                    default:
                        ValidateCommand(node, catalog, textKeyExists, errors);
                        break;
                }
            }
        }

        private static void ValidateCommand(
            TutorialNode node,
            TutorialCommandCatalog catalog,
            Func<string, bool> textKeyExists,
            List<TutorialScriptError> errors)
        {
            var line = node.Line;
            var arguments = node.Arguments ?? Array.Empty<string>();
            switch (node.Command)
            {
                case TutorialPrimitives.Say:
                    if (Expect(node, 1, "say <text-key> [continue=tap|none] [target=<id>] [side=auto|above|below|left|right] [speaker=<text-key>] [arrow=on|off]", errors))
                    {
                        CheckTextKey(line, arguments[0], textKeyExists, errors);
                        var speaker = node.GetNamed(TutorialPrimitives.SpeakerArgument);
                        if (speaker != null)
                            CheckTextKey(line, speaker, textKeyExists, errors);
                    }

                    CheckNamed(node, errors, TutorialPrimitives.ContinueArgument, TutorialPrimitives.TargetArgument,
                        TutorialPrimitives.SideArgument, TutorialPrimitives.SpeakerArgument, TutorialPrimitives.ArrowArgument);
                    CheckChoice(node, TutorialPrimitives.ContinueArgument, ContinueModes, errors);
                    CheckChoice(node, TutorialPrimitives.SideArgument, Sides, errors);
                    CheckFlag(node, TutorialPrimitives.ArrowArgument, errors);
                    return;
                case TutorialPrimitives.Highlight:
                    Expect(node, 1, "highlight <target-id> [padding=<px>] [hand=on|off] [timeout=<seconds>]", errors);
                    CheckNamed(node, errors, TutorialPrimitives.PaddingArgument, TutorialPrimitives.HandArgument, TutorialPrimitives.TimeoutArgument);
                    CheckNumber(node, TutorialPrimitives.PaddingArgument, errors);
                    CheckTimeout(node, errors);
                    CheckFlag(node, TutorialPrimitives.HandArgument, errors);
                    return;
                case TutorialPrimitives.ClearHighlight:
                case TutorialPrimitives.Show:
                case TutorialPrimitives.Hide:
                case TutorialPrimitives.Close:
                    Expect(node, 0, node.Command, errors);
                    CheckNamed(node, errors);
                    return;
                case TutorialPrimitives.WaitTarget:
                    Expect(node, 1, "wait_target <target-id> [timeout=<seconds>]", errors);
                    CheckNamed(node, errors, TutorialPrimitives.TimeoutArgument);
                    CheckTimeout(node, errors);
                    return;
                case TutorialPrimitives.WaitClick:
                    Expect(node, 1, "wait_click <target-id> [timeout=<seconds>]", errors);
                    CheckNamed(node, errors, TutorialPrimitives.TimeoutArgument);
                    CheckTimeout(node, errors);
                    return;
                case TutorialPrimitives.Delay:
                    if (Expect(node, 1, "delay <seconds>", errors)
                        && (!TryParseNumber(arguments[0], out var seconds) || seconds < 0f))
                        errors.Add(new TutorialScriptError(line, $"'delay' needs a non-negative number of seconds, got '{arguments[0]}'."));
                    CheckNamed(node, errors);
                    return;
                case TutorialPrimitives.Action:
                    CheckNamed(node, errors);
                    ValidateKeyed(node, TutorialCommandCategory.Action, false, catalog, errors);
                    return;
                case TutorialPrimitives.Wait:
                    CheckNamed(node, errors);
                    ValidateKeyed(node, TutorialCommandCategory.Wait, false, catalog, errors);
                    return;
                case TutorialPrimitives.WaitUntil:
                    CheckNamed(node, errors);
                    ValidateKeyed(node, TutorialCommandCategory.Condition, true, catalog, errors);
                    return;
                case TutorialPrimitives.SkipIf:
                    CheckNamed(node, errors);
                    ValidateKeyed(node, TutorialCommandCategory.Condition, true, catalog, errors);
                    return;
                default:
                    errors.Add(new TutorialScriptError(line, $"Unknown command '{node.Command}'."));
                    return;
            }
        }

        private static void ValidateKeyed(
            TutorialNode node,
            TutorialCommandCategory category,
            bool allowNot,
            TutorialCommandCatalog catalog,
            List<TutorialScriptError> errors)
        {
            var arguments = node.Arguments ?? Array.Empty<string>();
            var start = allowNot && arguments.Length > 0 && arguments[0] == TutorialScriptParser.NotKeyword ? 1 : 0;
            if (arguments.Length <= start)
            {
                errors.Add(new TutorialScriptError(node.Line, $"'{node.Command}' needs a {category.ToString().ToLowerInvariant()} key."));
                return;
            }

            var rest = new string[arguments.Length - start - 1];
            Array.Copy(arguments, start + 1, rest, 0, rest.Length);
            ValidateGameCommand(node.Line, category, arguments[start], rest, catalog, errors);
        }

        private static void ValidateGameCommand(
            int line,
            TutorialCommandCategory category,
            string key,
            string[] arguments,
            TutorialCommandCatalog catalog,
            List<TutorialScriptError> errors)
        {
            if (catalog == null)
                return;

            var name = category.ToString().ToLowerInvariant();
            if (!catalog.TryGet(category, key, out var signature))
            {
                errors.Add(new TutorialScriptError(line, $"Unknown {name} '{key}'."));
                return;
            }

            var count = arguments?.Length ?? 0;
            if (count < signature.RequiredCount || count > signature.Parameters.Count)
                errors.Add(new TutorialScriptError(line, $"Wrong number of arguments for {name} '{key}': expected '{signature.Usage}'."));
        }

        private static bool Expect(TutorialNode node, int count, string usage, List<TutorialScriptError> errors)
        {
            if ((node.Arguments?.Length ?? 0) == count)
                return true;

            errors.Add(new TutorialScriptError(node.Line, $"Expected '{usage}'."));
            return false;
        }

        private static void CheckNamed(TutorialNode node, List<TutorialScriptError> errors, params string[] allowed)
        {
            if (node.NamedArguments == null)
                return;

            foreach (var argument in node.NamedArguments)
            {
                if (Array.IndexOf(allowed, argument.Name) < 0)
                    errors.Add(new TutorialScriptError(node.Line, $"'{node.Command}' has no option '{argument.Name}'."));
            }
        }

        private static void CheckChoice(TutorialNode node, string name, string[] allowed, List<TutorialScriptError> errors)
        {
            var value = node.GetNamed(name);
            if (value != null && Array.IndexOf(allowed, value) < 0)
                errors.Add(new TutorialScriptError(node.Line, $"'{name}' must be one of {string.Join(", ", allowed)}, got '{value}'."));
        }

        private static void CheckNumber(TutorialNode node, string name, List<TutorialScriptError> errors)
        {
            var value = node.GetNamed(name);
            if (value != null && !TryParseNumber(value, out _))
                errors.Add(new TutorialScriptError(node.Line, $"'{name}' must be a number, got '{value}'."));
        }

        // A negative timeout is not a crash, but it would fire on the next frame, which is never what an author meant.
        private static void CheckTimeout(TutorialNode node, List<TutorialScriptError> errors)
        {
            var value = node.GetNamed(TutorialPrimitives.TimeoutArgument);
            if (value != null && (!TryParseNumber(value, out var seconds) || seconds < 0f))
                errors.Add(new TutorialScriptError(node.Line, $"'timeout' must be a non-negative number of seconds, got '{value}'."));
        }

        private static void CheckFlag(TutorialNode node, string name, List<TutorialScriptError> errors)
        {
            var value = node.GetNamed(name);
            if (value == null)
                return;

            if (TutorialArguments.ParseFlag(value, true) == TutorialArguments.ParseFlag(value, false))
                return;

            errors.Add(new TutorialScriptError(node.Line, $"'{name}' must be on or off, got '{value}'."));
        }

        private static void CheckTextKey(int line, string key, Func<string, bool> textKeyExists, List<TutorialScriptError> errors)
        {
            if (textKeyExists != null && !textKeyExists(key))
                errors.Add(new TutorialScriptError(line, $"Unknown text key '{key}'."));
        }

        private static CloseFlow AnalyzeNodes(TutorialNode[] nodes, int initialStates)
        {
            var flow = new CloseFlow(initialStates);
            if (nodes == null)
                return flow;

            foreach (var node in nodes)
            {
                if (flow.Continuing == 0)
                    break;

                var next = AnalyzeNode(node, flow.Continuing);
                flow = new CloseFlow(next.Continuing, flow.Exited | next.Exited);
            }

            return flow;
        }

        private static CloseFlow AnalyzeNode(TutorialNode node, int states)
        {
            if (node.Kind == TutorialNodeKind.If)
            {
                var whenTrue = AnalyzeNodes(node.Children, states);
                var whenFalse = node.ElseChildren == null
                    ? new CloseFlow(states)
                    : AnalyzeNodes(node.ElseChildren, states);
                return new CloseFlow(
                    whenTrue.Continuing | whenFalse.Continuing,
                    whenTrue.Exited | whenFalse.Exited);
            }

            if (node.Kind == TutorialNodeKind.Race)
            {
                if (node.Children == null || node.Children.Length == 0)
                    return new CloseFlow(states);

                var race = new CloseFlow(0);
                foreach (var branch in node.Children)
                {
                    var branchFlow = AnalyzeNodes(new[] { branch }, states);
                    race = new CloseFlow(
                        race.Continuing | branchFlow.Continuing,
                        race.Exited | branchFlow.Exited);
                }

                return race;
            }

            if (node.Command == TutorialPrimitives.Close)
                return new CloseFlow(OverlayClosed);
            if (node.Command == TutorialPrimitives.Say
                || node.Command == TutorialPrimitives.Highlight
                || node.Command == TutorialPrimitives.Show)
                return new CloseFlow(OverlayOpen);
            if (node.Command == TutorialPrimitives.SkipIf)
                return new CloseFlow(states, states);

            return new CloseFlow(states);
        }

        private readonly struct CloseFlow
        {
            public int Continuing { get; }
            public int Exited { get; }
            public int AllStates => Continuing | Exited;

            public CloseFlow(int continuing, int exited = 0)
            {
                Continuing = continuing;
                Exited = exited;
            }
        }
    }
}
