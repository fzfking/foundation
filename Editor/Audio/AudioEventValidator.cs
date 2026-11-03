using System;
using System.Collections.Generic;
using Foundation.TypedIds;
using Object = UnityEngine.Object;

namespace Foundation.Audio.Editor
{
    public enum AudioIssueSeverity
    {
        Error,

        Warning,
    }

    public readonly struct AudioValidationIssue
    {
        public AudioIssueSeverity Severity { get; }
        public string EventId { get; }
        public string Message { get; }
        public Object Context { get; }

        public AudioValidationIssue(AudioIssueSeverity severity, string eventId, string message, Object context)
        {
            Severity = severity;
            EventId = eventId;
            Message = message;
            Context = context;
        }

        public override string ToString() => $"{Severity}: [{EventId}] {Message}";
    }

    /// <summary>The content rules of audio events, independent of any window. Pure: no asset database access.</summary>
    public static class AudioEventValidator
    {
        private const float MaxReasonableCooldown = 5f;
        private const float MinReasonablePitch = 0.25f;
        private const float MaxReasonablePitch = 3f;

        /// <param name="definitions">Every event, from every library, so duplicates across libraries are found.</param>
        /// <param name="owners">The asset each definition lives in, parallel to <paramref name="definitions"/> (may be null).</param>
        /// <param name="knownChannels">The channel ids that exist, or null when the project defines them in code and they cannot be checked.</param>
        public static List<AudioValidationIssue> Validate(
            IReadOnlyList<AudioEventDefinition> definitions,
            IReadOnlyList<Object> owners,
            ICollection<string> knownChannels)
        {
            var issues = new List<AudioValidationIssue>();
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                var owner = owners != null && index < owners.Count ? owners[index] : null;
                var id = definition.Id.Value;
                var label = string.IsNullOrEmpty(id) ? definition.DisplayName : id;
                void Add(AudioIssueSeverity severity, string message) => issues.Add(new AudioValidationIssue(severity, label, message, owner));

                if (!StableIdKey.IsValid(id, out var reason))
                    Add(AudioIssueSeverity.Error, "Invalid id. " + reason);
                else if (seen.TryGetValue(id, out var first))
                    Add(AudioIssueSeverity.Error, $"Duplicate id: also defined by '{definitions[first].DisplayName}'. Only one can be registered.");
                else
                    seen.Add(id, index);

                ValidateChannel(definition, knownChannels, Add);
                ValidateClips(definition, Add);
                ValidateRanges(definition, Add);
            }

            return issues;
        }

        private static void ValidateChannel(AudioEventDefinition definition, ICollection<string> knownChannels, Action<AudioIssueSeverity, string> add)
        {
            if (definition.Channel.IsEmpty)
                add(AudioIssueSeverity.Error, "No channel selected: the event cannot play.");
            else if (knownChannels != null && !knownChannels.Contains(definition.Channel.Value))
                add(AudioIssueSeverity.Error, $"Channel '{definition.Channel.Value}' does not exist.");
        }

        private static void ValidateClips(AudioEventDefinition definition, Action<AudioIssueSeverity, string> add)
        {
            var clips = definition.Clips ?? Array.Empty<AudioClipEntry>();
            var usable = 0;
            for (var index = 0; index < clips.Length; index++)
            {
                if (clips[index].IsEmpty)
                    add(AudioIssueSeverity.Warning, $"Clip slot {index + 1} is empty.");
                else
                    usable++;
            }

            if (usable == 0)
            {
                add(AudioIssueSeverity.Error, "No clips: the event plays nothing.");
                return;
            }

            if (usable > 1 && definition.Selection == AudioSelectionMode.Single)
                add(AudioIssueSeverity.Warning, "Selection is Single, so only the first clip is ever played.");

            if (usable == 1 && definition.Selection != AudioSelectionMode.Single)
                add(AudioIssueSeverity.Warning, "The selection mode has no effect with one clip.");
        }

        private static void ValidateRanges(AudioEventDefinition definition, Action<AudioIssueSeverity, string> add)
        {
            var volume = definition.VolumeRange;
            if (volume.x > volume.y)
                add(AudioIssueSeverity.Error, "Volume minimum is above the maximum.");
            if (volume.x < 0f || volume.y > 1f)
                add(AudioIssueSeverity.Warning, "Volume outside 0..1 is clamped.");
            if (volume.y <= 0f)
                add(AudioIssueSeverity.Warning, "The event is silent (maximum volume is 0).");

            var pitch = definition.PitchRange;
            if (pitch.x <= 0f)
                add(AudioIssueSeverity.Error, "Pitch must be above 0.");
            if (pitch.x > pitch.y)
                add(AudioIssueSeverity.Error, "Pitch minimum is above the maximum.");
            if (pitch.x > 0f && (pitch.x < MinReasonablePitch || pitch.y > MaxReasonablePitch))
                add(AudioIssueSeverity.Warning, $"Pitch outside {MinReasonablePitch}..{MaxReasonablePitch} sounds unnatural.");

            if (definition.CooldownSeconds < 0f)
                add(AudioIssueSeverity.Error, "Cooldown must not be negative.");
            else if (definition.CooldownSeconds > MaxReasonableCooldown)
                add(AudioIssueSeverity.Warning, $"A cooldown above {MaxReasonableCooldown} s will swallow most requests.");

            if (definition.MaxPlaybacks < 0)
                add(AudioIssueSeverity.Error, "Max playbacks must not be negative.");

            if (definition.Spatial.Enabled && definition.Spatial.MaxDistance < definition.Spatial.MinDistance)
                add(AudioIssueSeverity.Error, "Spatial max distance is below the min distance.");
        }
    }
}
