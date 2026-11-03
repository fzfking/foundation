using System.Collections.Generic;
using Foundation.Randomization;
using UnityEditor;
using UnityEngine;
using System.IO;

namespace Foundation.Audio.Editor
{
    /// <summary>
    /// Plays clips and events in the Editor without Play Mode, with the volume, pitch and clip selection the
    /// runtime would use.
    /// </summary>
    public static class AudioPreviewPlayer
    {
        private static readonly List<AudioSource> Active = new();
        private static readonly Dictionary<string, int> LastClipByEvent = new();
        private static readonly IRandomService Random = new SystemRandomService();

        public static bool IsPlaying => Active.Count > 0;

        public static AudioSource Play(AudioClip clip, float volume = 1f, float pitch = 1f, bool loop = false)
        {
            if (clip == null)
                return null;

            var gameObject = EditorUtility.CreateGameObjectWithHideFlags("Audio Preview", HideFlags.HideAndDontSave, typeof(AudioSource));
            var source = gameObject.GetComponent<AudioSource>();
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);
            source.pitch = Mathf.Max(0.01f, pitch);
            source.loop = loop;
            source.spatialBlend = 0f;
            source.Play();
            Active.Add(source);
            return source;
        }

        /// <summary>Plays the event the way the runtime would: selection mode, random volume and pitch. Returns false when nothing could be previewed.</summary>
        public static bool PlayEvent(AudioEventDefinition definition, out string problem)
        {
            problem = null;
            var clips = definition.Clips;
            var last = LastClipByEvent.TryGetValue(definition.Id.Value, out var remembered) ? remembered : -1;
            var index = AudioClipSelector.Pick(definition.Selection, clips, last, Random);
            if (index < 0)
            {
                problem = "The event has no clips.";
                return false;
            }

            LastClipByEvent[definition.Id.Value] = index;
            var clip = ResolveClip(clips[index]);
            if (clip == null)
            {
                problem = $"Clip {index + 1} cannot be previewed: '{clips[index].AssetId}' is not an asset path or clip name.";
                return false;
            }

            var volume = definition.VolumeRange.y > definition.VolumeRange.x ? Random.NextFloat(definition.VolumeRange.x, definition.VolumeRange.y) : definition.VolumeRange.x;
            var pitch = definition.PitchRange.y > definition.PitchRange.x ? Random.NextFloat(definition.PitchRange.x, definition.PitchRange.y) : definition.PitchRange.x;
            Play(clip, volume, pitch, definition.Loop);
            return true;
        }

        /// <summary>A direct reference, or an asset id that is a project path or a clip name.</summary>
        public static AudioClip ResolveClip(AudioClipEntry entry)
        {
            if (entry.Clip != null)
                return entry.Clip;

            var id = entry.AssetId;
            if (string.IsNullOrEmpty(id))
                return null;

            if (id.StartsWith("Assets/") || id.StartsWith("Packages/"))
                return AssetDatabase.LoadAssetAtPath<AudioClip>(id);

            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip " + Path.GetFileNameWithoutExtension(id)))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip != null && clip.name == Path.GetFileNameWithoutExtension(id))
                    return clip;
            }

            return null;
        }

        public static void StopAll()
        {
            foreach (var source in Active)
            {
                if (source != null)
                    Object.DestroyImmediate(source.gameObject);
            }

            Active.Clear();
        }

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            EditorApplication.update += Cleanup;
            AssemblyReloadEvents.beforeAssemblyReload += StopAll;
            EditorApplication.playModeStateChanged += _ => StopAll();
        }

        private static void Cleanup()
        {
            for (var index = Active.Count - 1; index >= 0; index--)
            {
                var source = Active[index];
                if (source == null)
                {
                    Active.RemoveAt(index);
                }
                else if (!source.isPlaying && !source.loop)
                {
                    Object.DestroyImmediate(source.gameObject);
                    Active.RemoveAt(index);
                }
            }
        }
    }
}
